using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App;

public partial class MessageCenterWindow : Window
{
    public event EventHandler? MessagesChanged;

    public MessageCenterWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshAll();
    }

    private void RefreshAll(int? selectInboxId = null, int? selectSentId = null)
    {
        try
        {
            var inbox = UserMessageService.GetInbox();
            var sent = UserMessageService.GetSent();
            var recipients = UserMessageService.GetRecipients();

            InboxGrid.ItemsSource = inbox;
            SentGrid.ItemsSource = sent;
            RecipientBox.ItemsSource = recipients;

            if (RecipientBox.SelectedItem is null && recipients.Count > 0)
                RecipientBox.SelectedIndex = 0;

            InboxGrid.SelectedItem = selectInboxId.HasValue ? inbox.FirstOrDefault(x => x.Id == selectInboxId.Value) : inbox.FirstOrDefault();
            SentGrid.SelectedItem = selectSentId.HasValue ? sent.FirstOrDefault(x => x.Id == selectSentId.Value) : sent.FirstOrDefault();

            ShowInboxDetails(InboxGrid.SelectedItem as UserMessageRow);
            ShowSentDetails(SentGrid.SelectedItem as UserMessageRow);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Hinweise konnten nicht geladen werden: " + ex.Message;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll(
        (InboxGrid.SelectedItem as UserMessageRow)?.Id,
        (SentGrid.SelectedItem as UserMessageRow)?.Id);

    private void InboxGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowInboxDetails(InboxGrid.SelectedItem as UserMessageRow);

    private void SentGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowSentDetails(SentGrid.SelectedItem as UserMessageRow);

    private void ShowInboxDetails(UserMessageRow? row)
    {
        InboxMeta.Text = row is null ? "Kein Hinweis ausgewählt." : $"Von {row.Partner} · {row.CreatedText} · {row.Priority}";
        InboxSubject.Text = row?.Subject ?? string.Empty;
        InboxBody.Text = row?.Body ?? string.Empty;
        AcknowledgeButton.IsEnabled = row is not null && !row.IsAcknowledged;
        AcknowledgeButton.Content = row?.IsAcknowledged == true ? "Bereits bestätigt" : "Gelesen bestätigen";
    }

    private void ShowSentDetails(UserMessageRow? row)
    {
        SentMeta.Text = row is null ? "Kein Hinweis ausgewählt." : $"An {row.Partner} · {row.CreatedText} · {row.Priority}";
        SentSubject.Text = row?.Subject ?? string.Empty;
        SentBody.Text = row?.Body ?? string.Empty;
        SentStatus.Text = row?.StatusText ?? string.Empty;
    }

    private void AcknowledgeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (InboxGrid.SelectedItem is not UserMessageRow row)
            return;

        if (UserMessageService.Acknowledge(row.Id))
        {
            StatusText.Text = "Der Hinweis wurde als gelesen bestätigt.";
            RefreshAll(selectInboxId: row.Id);
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (RecipientBox.SelectedItem is not UserMessageRecipient recipient)
        {
            StatusText.Text = "Bitte einen Empfänger auswählen.";
            return;
        }

        try
        {
            var priority = (PriorityBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            var sent = UserMessageService.Send(recipient.Id, SubjectBox.Text, BodyBox.Text, priority);
            SubjectBox.Clear();
            BodyBox.Clear();
            PriorityBox.SelectedIndex = 0;
            StatusText.Text = $"Hinweis an {recipient.DisplayName} gesendet.";
            RefreshAll(selectSentId: sent.Id);
            MessageTabs.SelectedIndex = 1;
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
