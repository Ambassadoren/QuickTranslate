using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickTranslate;

public partial class HistoryWindow : Window
{
    private readonly HistoryStore store;
    private bool initialized;
    internal HistoryEntry? Selected { get; private set; }
    internal HistoryWindow(HistoryStore store, bool enabled)
    {
        InitializeComponent();
        DarkChrome.Attach(this);
        this.store = store;
        InfoText.Text = enabled ? "Последние 50 переводов" : "Сохранение выключено";
        initialized = true;
        Refresh();
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 30);
    }
    private void Refresh()
    {
        var entries = store.Search(SearchBox.Text).ToList();
        EntriesList.ItemsSource = entries;
        EmptyLabel.Text = store.Entries.Count == 0 ? "История пока пуста" : "Ничего не найдено";
        EmptyLabel.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (entries.Count > 0) EntriesList.SelectedIndex = 0;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (initialized) Refresh(); }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        var entry = EntriesList.SelectedItem as HistoryEntry;
        PreviewBox.Text = entry == null ? "" : entry.Source + "\n\n→\n\n" + entry.Translation;
        CopyButton.IsEnabled = OpenButton.IsEnabled = entry != null;
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not HistoryEntry entry) return;
        Selected = entry;
        DialogResult = true;
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not HistoryEntry entry) return;
        try { Clipboard.SetText(entry.Translation); InfoText.Text = "Перевод скопирован"; }
        catch { InfoText.Text = "Буфер занят. Повторите попытку."; }
    }
    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (store.Entries.Count == 0) return;
        if (MessageBox.Show(this, "Удалить все сохранённые переводы? Отменить очистку нельзя.", "Очистка истории", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await store.ClearAsync(); Refresh(); InfoText.Text = "История очищена"; }
        catch (Exception ex) { InfoText.Text = "Не удалось очистить историю: " + ex.Message; }
    }
}
