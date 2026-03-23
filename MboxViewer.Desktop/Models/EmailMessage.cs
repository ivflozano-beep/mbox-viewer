using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MboxViewer.Desktop.Models;

public sealed class EmailMessage : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isStarred;
    private bool _isArchived;
    private bool _isDeleted;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Subject { get; set; } = "(Sin asunto)";

    public string FromName { get; set; } = "Desconocido";

    public string FromAddress { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string Cc { get; set; } = string.Empty;

    public DateTimeOffset? Date { get; set; }

    public string Preview { get; set; } = string.Empty;

    public string PlainTextBody { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string HeadersText { get; set; } = string.Empty;

    public ObservableCollection<string> Labels { get; } = [];

    public bool IsImportant { get; set; }

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (_isRead == value)
            {
                return;
            }

            _isRead = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusGlyph));
        }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set
        {
            if (_isStarred == value)
            {
                return;
            }

            _isStarred = value;
            OnPropertyChanged();
        }
    }

    public bool IsArchived
    {
        get => _isArchived;
        set
        {
            if (_isArchived == value)
            {
                return;
            }

            _isArchived = value;
            OnPropertyChanged();
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value)
            {
                return;
            }

            _isDeleted = value;
            OnPropertyChanged();
        }
    }

    public string DisplaySender => string.IsNullOrWhiteSpace(FromName) ? FromAddress : FromName;

    public string DisplayDate => Date?.ToLocalTime().ToString("dd MMM") ?? string.Empty;

    public string FullDate => Date?.ToLocalTime().ToString("dddd, dd MMMM yyyy HH:mm") ?? "Fecha desconocida";

    public string StatusGlyph => IsRead ? " " : "?";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
