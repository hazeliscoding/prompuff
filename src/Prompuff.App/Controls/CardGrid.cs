using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Prompuff.App.Controls;

/// <summary>One row of cards in a <see cref="CardGrid"/>.</summary>
public sealed class CardRow(IReadOnlyList<object?> cards)
{
    public IReadOnlyList<object?> Cards { get; } = cards;
}

/// <summary>
/// Cards in as many equal columns as fit at <see cref="MinItemWidth"/>, like <see cref="AdaptiveGrid"/>, for lists too
/// long to lay out at once. The <see cref="Cards"/> are grouped into rows of <see cref="ColumnCount"/>, and a
/// <see cref="VirtualizingStackPanel"/> creates only the rows on screen and recycles them while scrolling, so 10,000
/// prompts cost about as much as the few dozen that show. Each row is as tall as its tallest card.
/// </summary>
public sealed class CardGrid : ItemsControl
{
    public static readonly StyledProperty<IList?> CardsProperty =
        AvaloniaProperty.Register<CardGrid, IList?>(nameof(Cards));

    public static readonly StyledProperty<IDataTemplate?> CardTemplateProperty =
        AvaloniaProperty.Register<CardGrid, IDataTemplate?>(nameof(CardTemplate));

    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(MinItemWidth), 272);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardGrid, double>(nameof(Spacing), 12);

    private static readonly FuncTemplate<Panel?> RowsPanel = new(() => new VirtualizingStackPanel());

    private INotifyCollectionChanged? _observed;
    private bool _rowsStale = true;
    private double _width = double.NaN;

    static CardGrid()
    {
        ItemsPanelProperty.OverrideDefaultValue<CardGrid>(RowsPanel);
        AffectsMeasure<CardGrid>(MinItemWidthProperty, SpacingProperty);
    }

    /// <summary>The cards, in order. Changes to an observable list regroup the rows at the next layout.</summary>
    public IList? Cards
    {
        get => GetValue(CardsProperty);
        set => SetValue(CardsProperty, value);
    }

    /// <summary>Builds one card.</summary>
    public IDataTemplate? CardTemplate
    {
        get => GetValue(CardTemplateProperty);
        set => SetValue(CardTemplateProperty, value);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many cards each row holds, so arrow keys can move up and down a row.</summary>
    public int ColumnCount { get; private set; } = 1;

    /// <summary>The width rows lay their cards out across, from the last layout.</summary>
    internal double RowWidth => double.IsFinite(_width) ? _width : ColumnCount * MinItemWidth + (ColumnCount - 1) * Spacing;

    /// <summary>
    /// Scrolls the row holding <paramref name="card"/> into view, creating it if it was off screen, and returns the
    /// card's control, or null when the card isn't in the grid or the grid isn't showing.
    /// </summary>
    public Control? BringCardIntoView(object card)
    {
        UpdateRows();
        var index = Cards?.IndexOf(card) ?? -1;
        if (index < 0)
        {
            return null;
        }

        var row = index / ColumnCount;
        ScrollIntoView(row);
        return (ContainerFromIndex(row) as CardRowPresenter)?.ControlFor(card);
    }

    /// <summary>The cards that have controls right now: those on screen, and a few rows either side.</summary>
    public IEnumerable<Control> GetRealizedCards() =>
        GetRealizedContainers().OfType<CardRowPresenter>().SelectMany(row => row.CardControls);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardsProperty)
        {
            if (_observed is not null)
            {
                _observed.CollectionChanged -= OnCardsChanged;
            }

            _observed = change.NewValue as INotifyCollectionChanged;
            if (_observed is not null)
            {
                _observed.CollectionChanged += OnCardsChanged;
            }

            MarkRowsStale();
        }
        else if (change.Property == CardTemplateProperty)
        {
            // Rows build their cards from the template, so start them afresh.
            ItemsSource = null;
            MarkRowsStale();
        }
        else if (change.Property == SpacingProperty)
        {
            // Rows take their spacing when they're prepared.
            MarkRowsStale();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width))
        {
            _width = availableSize.Width;
        }

        UpdateRows();
        return base.MeasureOverride(availableSize);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = DefaultRecycleKey;
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new CardRowPresenter(this);

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is CardRowPresenter row)
        {
            row.Show(item as CardRow, index);
        }
    }

    protected override void ContainerIndexChangedOverride(Control container, int oldIndex, int newIndex)
    {
        base.ContainerIndexChangedOverride(container, oldIndex, newIndex);
        if (container is CardRowPresenter row)
        {
            row.Index = newIndex;
        }
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkRowsStale();

    private void MarkRowsStale()
    {
        _rowsStale = true;
        InvalidateMeasure();
    }

    /// <summary>Groups the cards into rows again if they changed or a different number of columns fits.</summary>
    private void UpdateRows()
    {
        var columns = double.IsFinite(_width)
            ? Math.Max(1, (int)((_width + Spacing) / (MinItemWidth + Spacing)))
            : ColumnCount;
        if (!_rowsStale && columns == ColumnCount && ItemsSource is not null)
        {
            return;
        }

        _rowsStale = false;
        ColumnCount = columns;
        var cards = Cards;
        var count = cards?.Count ?? 0;
        var rows = new List<CardRow>((count + columns - 1) / columns);
        for (var start = 0; start < count; start += columns)
        {
            var row = new object?[Math.Min(columns, count - start)];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = cards![start + i];
            }

            rows.Add(new CardRow(row));
        }

        ItemsSource = rows;
    }

    /// <summary>One row of a <see cref="CardGrid"/>: its cards side by side in equal columns.</summary>
    private sealed class CardRowPresenter(CardGrid owner) : Panel
    {
        private int _index;
        private IDataTemplate? _template;

        public int Index
        {
            get => _index;
            set
            {
                _index = value;

                // Rows after the first keep the grid's spacing above them.
                Margin = new Thickness(0, value == 0 ? 0 : owner.Spacing, 0, 0);
            }
        }

        public IEnumerable<Control> CardControls => Children.Where(child => child.IsVisible);

        public Control? ControlFor(object card) => Children.FirstOrDefault(child => child.IsVisible && ReferenceEquals(child.DataContext, card));

        /// <summary>
        /// Shows another row's cards. Building a card is the expensive part of scrolling, so each card control is built
        /// once from the template and kept, even while the row waits to be reused, and later only given another prompt.
        /// </summary>
        public void Show(CardRow? row, int index)
        {
            Index = index;
            if (!ReferenceEquals(_template, owner.CardTemplate))
            {
                Children.Clear();
                _template = owner.CardTemplate;
            }

            var cards = row?.Cards ?? [];
            for (var i = 0; i < cards.Count; i++)
            {
                if (i < Children.Count)
                {
                    Children[i].DataContext = cards[i];
                    Children[i].IsVisible = true;
                }
                else if (_template?.Build(cards[i]) is { } card)
                {
                    card.DataContext = cards[i];
                    Children.Add(card);
                }
            }

            // A short last row hides the cards it doesn't need rather than throwing them away.
            for (var i = cards.Count; i < Children.Count; i++)
            {
                Children[i].IsVisible = false;
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var (columns, itemWidth, width) = Layout(availableSize.Width);
            var height = 0d;
            for (var i = 0; i < Children.Count && i < columns; i++)
            {
                var child = Children[i];
                if (child.IsVisible)
                {
                    child.Measure(new Size(itemWidth, double.PositiveInfinity));
                    height = Math.Max(height, child.DesiredSize.Height);
                }
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var (columns, itemWidth, _) = Layout(finalSize.Width);
            var spacing = owner.Spacing;
            for (var i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child.IsVisible && i < columns)
                {
                    child.Arrange(new Rect(i * (itemWidth + spacing), 0, itemWidth, finalSize.Height));
                }
            }

            return finalSize;
        }

        private (int Columns, double ItemWidth, double Width) Layout(double available)
        {
            var width = double.IsFinite(available) ? available : owner.RowWidth;
            var columns = owner.ColumnCount;
            return (columns, Math.Max(0, (width - owner.Spacing * (columns - 1)) / columns), width);
        }
    }
}
