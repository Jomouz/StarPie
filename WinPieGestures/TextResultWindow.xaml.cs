using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StarPie.Plugin;

namespace WinPieGestures;

/// <summary>Host-themed text results; explicit choices are returned to the requesting plugin.</summary>
internal partial class TextResultWindow : Window
{
    private readonly IReadOnlyList<TextResultItem> _items;
    private readonly string _originalTitle;
    private readonly string? _titleKey;
    private bool _refreshing;
    private readonly DependencyPropertyDescriptor _copyBackground = DependencyPropertyDescriptor.FromProperty(
        Control.BackgroundProperty, typeof(Button))!;
    internal TextResultSelection Selection { get; private set; } = new();

    internal TextResultWindow(string title, IReadOnlyList<TextResultItem> items, string? titleKey = null)
    {
        _items = items;
        _originalTitle = title;
        _titleKey = titleKey;
        InitializeComponent();
        Width = items.Count == 1 ? 640 : 820;
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 32);
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        // Apply after XAML content exists so the shared caption wraps the actual result UI.
        AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");
        RefreshLanguage();
        ApplyLayout(Width);
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        _copyBackground.AddValueChanged(CopyButton, EnsureCopyContrast);
        EnsureCopyContrast(this, EventArgs.Empty);
        I18n.LanguageChanged += RefreshLanguage;
    }

    internal void RefreshLanguage()
    {
        Title = string.IsNullOrEmpty(_titleKey) ? _originalTitle : I18n.T(_titleKey);
        HeadingText.Text = I18n.T("TextResultHeading");
        SummaryText.Text = I18n.TF(_items.Count == 1 ? "TextResultCountSingle" : "TextResultCount", _items.Count);
        ListHeadingText.Text = I18n.T("TextResultListHeading");
        ContentLabelText.Text = I18n.T("TextResultFullContent");
        KeyboardHintText.Text = I18n.T("TextResultKeyboardHint");
        CopyButton.Content = I18n.T("TextResultCopy");
        OpenButton.Content = I18n.T("TextResultOpen");
        CloseButton.Content = I18n.T("TextResultClose");
        OpenButton.ToolTip = I18n.T("TextResultOpenHint");
        int selected = Math.Max(0, ResultList.SelectedIndex);
        _refreshing = true;
        ResultRow[] rows = _items.Select((item, index) => new ResultRow(
            I18n.TF("TextResultRowLabel", index + 1, TextResultUrl.TryGetWebUrl(item.Text, out _) ?
                I18n.T("TextResultTypeLink") : I18n.T("TextResultTypeText")),
            item.Text.Replace('\r', ' ').Replace('\n', ' '))).ToArray();
        ResultList.ItemsSource = rows;
        ResultSelector.ItemsSource = rows;
        ResultList.SelectedIndex = Math.Min(selected, _items.Count - 1);
        ResultSelector.SelectedIndex = ResultList.SelectedIndex;
        _refreshing = false;
        UpdateSelection();
    }

    internal void ApplyLayout(double width)
    {
        bool multiple = _items.Count > 1;
        bool stacked = multiple && width < 680;
        ResultRoot.Margin = stacked ? new Thickness(20, 16, 20, 16) : new Thickness(24);
        ResultHeader.Margin = new Thickness(0, 0, 0, stacked ? 14 : 20);
        ResultFooter.Margin = new Thickness(0, stacked ? 14 : 20, 0, 0);
        ResultFooter.Padding = new Thickness(0, stacked ? 8 : 12, 0, 0);
        ResultSelector.Margin = new Thickness(0, 0, 0, stacked ? 12 : 16);
        ResultAddress.Margin = new Thickness(0, stacked ? 8 : 12, 0, 0);
        ResultSidebar.Visibility = multiple && !stacked ? Visibility.Visible : Visibility.Collapsed;
        ResultSelector.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = multiple && !stacked ? new GridLength(220) : new GridLength(1, GridUnitType.Star);
        GapColumn.Width = new GridLength(multiple && !stacked ? 20 : 0);
        DetailColumn.Width = multiple && !stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        TopRow.Height = new GridLength(1, GridUnitType.Star);
        BottomRow.Height = new GridLength(0);
        Grid.SetRow(ResultDetails, 0);
        Grid.SetColumn(ResultDetails, multiple && !stacked ? 2 : 0);
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        ResultSelector.SelectedIndex = ResultList.SelectedIndex;
        UpdateSelection();
    }

    private void ResultSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) ResultList.SelectedIndex = ResultSelector.SelectedIndex;
    }

    private void UpdateSelection()
    {
        int index = ResultList.SelectedIndex;
        if (index < 0) return;
        TextResultItem item = _items[index];
        if (ResultContent.Text != item.Text) ResultContent.Text = item.Text;
        bool isLink = TextResultUrl.TryGetWebUrl(item.Text, out string url);
        OpenButton.IsEnabled = isLink;
        FormatText.Text = item.Format == "QR Code" ? I18n.T("TextResultFormatQr") : item.Format;
        AddressText.Text = isLink ? I18n.TF("TextResultWebsite", new Uri(url).IdnHost) : I18n.T("TextResultTypeText");
        ActionHintText.Text = I18n.T(isLink ? "TextResultLinkHint" : "TextResultTextHint");
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Finish(TextResultOperation.Copy);
    private void Open_Click(object sender, RoutedEventArgs e) => Finish(TextResultOperation.Open);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Finish(TextResultOperation operation)
    {
        if (ResultList.SelectedIndex < 0 || operation == TextResultOperation.Open && !OpenButton.IsEnabled) return;
        Selection = new TextResultSelection { Index = ResultList.SelectedIndex, Operation = operation };
        Close();
    }

    private void EnsureCopyContrast(object? sender, EventArgs e)
    {
        if (CopyButton.Background is not SolidColorBrush background ||
            FindResource("AccentTextBrush") is not SolidColorBrush preferred) return;
        // Preserve the theme accent. Bright accent colors need dark text to keep small labels readable.
        CopyButton.Foreground = Contrast(preferred.Color, background.Color) >= 4.5 ? preferred :
            Contrast(Colors.White, background.Color) >= Contrast(Colors.Black, background.Color) ? Brushes.White : Brushes.Black;
    }

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                double channel = value / 255d;
                return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        double left = Luminance(first), right = Luminance(second);
        return (Math.Max(left, right) + .05) / (Math.Min(left, right) + .05);
    }

    protected override void OnClosed(EventArgs e)
    {
        I18n.LanguageChanged -= RefreshLanguage;
        _copyBackground.RemoveValueChanged(CopyButton, EnsureCopyContrast);
        base.OnClosed(e);
    }

    private sealed record ResultRow(string Label, string Summary);
}
