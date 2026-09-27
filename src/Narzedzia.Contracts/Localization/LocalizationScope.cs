using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Narzedzia.Contracts.Localization;

/// <summary>
/// Lokalizuje teksty istniejących kontrolek bez naruszania ich bindingów.
/// Zapamiętuje wartość źródłową i tłumaczy także późniejsze komunikaty z ViewModeli.
/// </summary>
public sealed class LocalizationScope : IDisposable
{
    private readonly Control _root;
    private readonly Dictionary<AvaloniaObject, ElementState> _states = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ILogical, LogicalTreeSubscription> _logicalTreeSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AvaloniaObject, CollectionSubscription> _auxiliaryCollectionSubscriptions =
        new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer _structureScanTimer;
    private bool _active;
    private bool _scanQueued;
    private bool _disposed;

    private LocalizationScope(Control root)
    {
        _root = root;
        _structureScanTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _structureScanTimer.Tick += OnStructureScanTimer;
        _root.AttachedToVisualTree += OnAttached;
        _root.DetachedFromVisualTree += OnDetached;
        Activate();
    }

    public static LocalizationScope Attach(Control root) => new(root);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Deactivate();
        _disposed = true;
        _root.AttachedToVisualTree -= OnAttached;
        _root.DetachedFromVisualTree -= OnDetached;
        _structureScanTimer.Tick -= OnStructureScanTimer;

        foreach (var subscription in _logicalTreeSubscriptions.Values)
        {
            subscription.Children.CollectionChanged -= subscription.Handler;
        }
        _logicalTreeSubscriptions.Clear();
        foreach (var subscription in _auxiliaryCollectionSubscriptions.Values)
        {
            subscription.Collection.CollectionChanged -= subscription.Handler;
        }
        _auxiliaryCollectionSubscriptions.Clear();

        foreach (var (element, state) in _states)
        {
            element.PropertyChanged -= state.PropertyChangedHandler;
        }

        _states.Clear();
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => Activate();

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => Deactivate();

    private void Activate()
    {
        if (_disposed)
        {
            return;
        }

        if (_active)
        {
            QueueScan();
            return;
        }

        _active = true;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        QueueScan();
    }

    private void Deactivate()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        LocalizationManager.LanguageChanged -= OnLanguageChanged;
        _structureScanTimer.Stop();
    }

    private void OnLogicalTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_active || _disposed)
        {
            return;
        }

        // Listy, DataGridy i palety potrafią dodać wiele kontrolek w jednej
        // klatce. Krótki debounce scala całą serię w jeden skan po uspokojeniu
        // drzewa zamiast obciążać renderowanie mapy przy każdym LayoutUpdated.
        _structureScanTimer.Stop();
        _structureScanTimer.Start();
    }

    private void OnStructureScanTimer(object? sender, EventArgs e)
    {
        _structureScanTimer.Stop();
        QueueScan();
    }

    private void QueueScan()
    {
        if (_scanQueued)
        {
            return;
        }

        _scanQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scanQueued = false;
            if (_active && !_disposed)
            {
                ScanAndRefresh();
            }
        }, DispatcherPriority.Background);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ScanAndRefresh();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_active && !_disposed)
                {
                    ScanAndRefresh();
                }
            });
        }
    }

    private void ScanAndRefresh()
    {
        if (!_active || _disposed)
        {
            return;
        }

        var liveElements = new HashSet<AvaloniaObject>(ReferenceEqualityComparer.Instance);
        var liveLogicalNodes = new HashSet<ILogical>(ReferenceEqualityComparer.Instance);
        if (_root is ILogical logicalRoot)
        {
            TrackLogicalSubtree(logicalRoot, liveLogicalNodes, liveElements);
        }
        else
        {
            TrackControl(_root, liveElements, liveLogicalNodes);
        }

        // Niektóre elementy szablonów są wyłącznie wizualne. Skanujemy je przy
        // zmianie struktury/języka, ale nie przy każdej klatce układu.
        foreach (var control in _root.GetVisualDescendants().OfType<Control>())
        {
            TrackControl(control, liveElements, liveLogicalNodes);
        }

        PruneDetachedState(liveElements, liveLogicalNodes);

        foreach (var state in _states.Values.ToArray())
        {
            foreach (var property in state.Properties)
            {
                Apply(state, property);
            }
        }
    }

    private void TrackLogicalSubtree(
        ILogical logical,
        HashSet<ILogical> liveLogicalNodes,
        HashSet<AvaloniaObject> liveElements)
    {
        if (!liveLogicalNodes.Add(logical))
        {
            return;
        }

        SubscribeToLogicalChildren(logical);
        if (logical is Control control)
        {
            TrackControl(control, liveElements, liveLogicalNodes);
        }

        foreach (var child in logical.LogicalChildren)
        {
            TrackLogicalSubtree(child, liveLogicalNodes, liveElements);
        }
    }

    private void SubscribeToLogicalChildren(ILogical logical)
    {
        if (logical.LogicalChildren is not INotifyCollectionChanged children)
        {
            return;
        }

        if (_logicalTreeSubscriptions.TryGetValue(logical, out var existing))
        {
            if (ReferenceEquals(existing.Children, children))
            {
                return;
            }

            existing.Children.CollectionChanged -= existing.Handler;
            _logicalTreeSubscriptions.Remove(logical);
        }

        NotifyCollectionChangedEventHandler handler = OnLogicalTreeChanged;
        children.CollectionChanged += handler;
        _logicalTreeSubscriptions.Add(logical, new LogicalTreeSubscription(children, handler));
    }

    private void PruneDetachedState(
        IReadOnlySet<AvaloniaObject> liveElements,
        IReadOnlySet<ILogical> liveLogicalNodes)
    {
        foreach (var element in _states.Keys.Where(element => !liveElements.Contains(element)).ToArray())
        {
            var state = _states[element];
            element.PropertyChanged -= state.PropertyChangedHandler;
            RestoreSourceValues(state);
            _states.Remove(element);
        }

        foreach (var logical in _logicalTreeSubscriptions.Keys
                     .Where(logical => !liveLogicalNodes.Contains(logical))
                     .ToArray())
        {
            var subscription = _logicalTreeSubscriptions[logical];
            subscription.Children.CollectionChanged -= subscription.Handler;
            _logicalTreeSubscriptions.Remove(logical);
        }

        foreach (var owner in _auxiliaryCollectionSubscriptions.Keys
                     .Where(owner => !liveElements.Contains(owner))
                     .ToArray())
        {
            var subscription = _auxiliaryCollectionSubscriptions[owner];
            subscription.Collection.CollectionChanged -= subscription.Handler;
            _auxiliaryCollectionSubscriptions.Remove(owner);
        }
    }

    private void TrackControl(
        Control control,
        HashSet<AvaloniaObject> liveElements,
        HashSet<ILogical> liveLogicalNodes)
    {
        liveElements.Add(control);

        TrackDataGridColumns(control, liveElements);

        if (control is TextBlock textBlock)
        {
            Track(textBlock, TextBlock.TextProperty, () => textBlock.Text,
                value => textBlock.SetCurrentValue(TextBlock.TextProperty, value));

            if (textBlock.Inlines is not null)
            {
                foreach (var run in textBlock.Inlines.OfType<Run>())
                {
                    liveElements.Add(run);
                    Track(run, Run.TextProperty, () => run.Text,
                        value => run.SetCurrentValue(Run.TextProperty, value));
                }
            }
        }

        if (control is TextBox textBox)
        {
            Track(textBox, TextBox.PlaceholderTextProperty, () => textBox.PlaceholderText,
                value => textBox.SetCurrentValue(TextBox.PlaceholderTextProperty, value));
        }

        if (control is ContentControl contentControl)
        {
            Track(contentControl, ContentControl.ContentProperty, () => contentControl.Content as string,
                value => contentControl.SetCurrentValue(ContentControl.ContentProperty, value));
        }

        if (control is HeaderedContentControl headeredControl)
        {
            Track(headeredControl, HeaderedContentControl.HeaderProperty, () => headeredControl.Header as string,
                value => headeredControl.SetCurrentValue(HeaderedContentControl.HeaderProperty, value));
        }
        else if (control is HeaderedSelectingItemsControl headeredSelectingControl)
        {
            Track(headeredSelectingControl, HeaderedSelectingItemsControl.HeaderProperty,
                () => headeredSelectingControl.Header as string,
                value => headeredSelectingControl.SetCurrentValue(
                    HeaderedSelectingItemsControl.HeaderProperty,
                    value));
        }
        else if (control is HeaderedItemsControl headeredItemsControl)
        {
            Track(headeredItemsControl, HeaderedItemsControl.HeaderProperty,
                () => headeredItemsControl.Header as string,
                value => headeredItemsControl.SetCurrentValue(HeaderedItemsControl.HeaderProperty, value));
        }

        if (control is Window window)
        {
            Track(window, Window.TitleProperty, () => window.Title,
                value => window.SetCurrentValue(Window.TitleProperty, value));
        }

        Track(control, ToolTip.TipProperty, () => ToolTip.GetTip(control) as string,
            value => control.SetCurrentValue(ToolTip.TipProperty, value),
            trackNull: control.IsSet(ToolTip.TipProperty));

        // ContextMenu jest renderowane w osobnym PopupRoot i nie pojawia się w
        // wizualnych potomkach głównego okna. Jawne przejście po jego logicznym
        // drzewie zapewnia lokalizację menu również przed pierwszym otwarciem.
        if (control.ContextMenu is ILogical contextMenu)
        {
            TrackLogicalSubtree(contextMenu, liveLogicalNodes, liveElements);
        }

        // MenuFlyout nie implementuje ILogical; jego pozycje robią to dopiero po
        // otwarciu popupu. Statycznie przypisane flyouty skanujemy więc z kolekcji.
        if (control.ContextFlyout is MenuFlyout menuFlyout)
        {
            liveElements.Add(menuFlyout);
            if (menuFlyout.Items is INotifyCollectionChanged observableItems)
            {
                SubscribeToAuxiliaryCollection(menuFlyout, observableItems);
            }
            foreach (var logicalItem in menuFlyout.Items.OfType<ILogical>())
            {
                TrackLogicalSubtree(logicalItem, liveLogicalNodes, liveElements);
            }
        }
    }

    private void TrackDataGridColumns(Control control, HashSet<AvaloniaObject> liveElements)
    {
        // Avalonia.Controls.DataGrid lives in an optional package that the shared
        // contracts assembly intentionally does not reference. Reflection keeps the
        // dependency boundary intact while still localizing column objects, which
        // are AvaloniaObjects but never appear as Controls in the logical tree.
        var dataGridType = control.GetType();
        while (dataGridType is not null &&
               !string.Equals(dataGridType.Name, "DataGrid", StringComparison.Ordinal))
        {
            dataGridType = dataGridType.BaseType;
        }
        if (dataGridType is null) return;
        var columnsProperty = control.GetType().GetProperty("Columns");
        if (columnsProperty?.GetValue(control) is not System.Collections.IEnumerable columns) return;
        if (columns is INotifyCollectionChanged observableColumns)
        {
            SubscribeToAuxiliaryCollection(control, observableColumns);
        }

        foreach (var columnObject in columns)
        {
            if (columnObject is not AvaloniaObject column) continue;
            var columnType = column.GetType();
            var headerAccessor = columnType.GetProperty("Header");
            var propertyOwner = columnType;
            System.Reflection.FieldInfo? headerField = null;
            while (propertyOwner is not null && headerField is null)
            {
                headerField = propertyOwner.GetField(
                    "HeaderProperty",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.FlattenHierarchy);
                propertyOwner = propertyOwner.BaseType;
            }
            if (headerAccessor is null || headerField?.GetValue(null) is not AvaloniaProperty headerProperty)
                continue;

            liveElements.Add(column);
            Track(column, headerProperty, () => column.GetValue(headerProperty) as string,
                value => column.SetCurrentValue(headerProperty, value));
        }
    }

    private void SubscribeToAuxiliaryCollection(
        AvaloniaObject owner,
        INotifyCollectionChanged collection)
    {
        if (_auxiliaryCollectionSubscriptions.TryGetValue(owner, out var existing))
        {
            if (ReferenceEquals(existing.Collection, collection))
            {
                return;
            }

            existing.Collection.CollectionChanged -= existing.Handler;
            _auxiliaryCollectionSubscriptions.Remove(owner);
        }

        NotifyCollectionChangedEventHandler handler = OnLogicalTreeChanged;
        collection.CollectionChanged += handler;
        _auxiliaryCollectionSubscriptions.Add(owner, new CollectionSubscription(collection, handler));
    }

    private ElementState GetOrCreateState(AvaloniaObject element)
    {
        if (_states.TryGetValue(element, out var state))
        {
            return state;
        }

        state = new ElementState(element);
        _states.Add(element, state);
        element.PropertyChanged += state.PropertyChangedHandler;
        state.PropertyChanged = (_, args) => OnTrackedPropertyChanged(state, args.Property);
        return state;
    }

    private void Track(
        AvaloniaObject element,
        AvaloniaProperty avaloniaProperty,
        Func<string?> read,
        Action<string> write,
        bool trackNull = true)
    {
        var value = read();
        if (value is null && !trackNull)
        {
            return;
        }

        var state = GetOrCreateState(element);
        if (state.Properties.Any(item => ReferenceEquals(item.Property, avaloniaProperty)))
        {
            return;
        }

        state.Properties.Add(new LocalizedProperty(avaloniaProperty, value, read, write));
    }

    private static void OnTrackedPropertyChanged(ElementState state, AvaloniaProperty changedProperty)
    {
        if (state.IsApplying)
        {
            return;
        }

        foreach (var property in state.Properties.Where(item => ReferenceEquals(item.Property, changedProperty)))
        {
            var current = property.Read();
            property.SourceValue = current;
            Apply(state, property);
        }
    }

    private static void Apply(ElementState state, LocalizedProperty property)
    {
        if (property.SourceValue is null)
        {
            return;
        }

        var expected = LocalizationManager.Translate(property.SourceValue);

        if (string.Equals(property.Read(), expected, StringComparison.Ordinal))
        {
            return;
        }

        state.IsApplying = true;
        try
        {
            property.Write(expected);
        }
        finally
        {
            state.IsApplying = false;
        }
    }

    private static void RestoreSourceValues(ElementState state)
    {
        state.IsApplying = true;
        try
        {
            foreach (var property in state.Properties)
            {
                if (property.SourceValue is { } sourceValue &&
                    !string.Equals(property.Read(), sourceValue, StringComparison.Ordinal))
                {
                    property.Write(sourceValue);
                }
            }
        }
        finally
        {
            state.IsApplying = false;
        }
    }

    private sealed class ElementState
    {
        public ElementState(AvaloniaObject element)
        {
            PropertyChangedHandler = (sender, args) => PropertyChanged?.Invoke(sender, args);
        }

        public bool IsApplying { get; set; }
        public List<LocalizedProperty> Properties { get; } = [];
        public EventHandler<AvaloniaPropertyChangedEventArgs> PropertyChangedHandler { get; }
        public EventHandler<AvaloniaPropertyChangedEventArgs>? PropertyChanged { get; set; }
    }

    private sealed class LocalizedProperty(
        AvaloniaProperty property,
        string? sourceValue,
        Func<string?> read,
        Action<string> write)
    {
        public AvaloniaProperty Property { get; } = property;
        public string? SourceValue { get; set; } = sourceValue;
        public Func<string?> Read { get; } = read;
        public Action<string> Write { get; } = write;
    }

    private sealed record LogicalTreeSubscription(
        INotifyCollectionChanged Children,
        NotifyCollectionChangedEventHandler Handler);

    private sealed record CollectionSubscription(
        INotifyCollectionChanged Collection,
        NotifyCollectionChangedEventHandler Handler);
}
