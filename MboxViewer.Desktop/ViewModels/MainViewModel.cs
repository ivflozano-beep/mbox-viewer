using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Microsoft.Win32;
using MboxViewer.Desktop.Models;
using MboxViewer.Desktop.Services;

namespace MboxViewer.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly MboxParser _parser = new();
    private readonly RelayCommand _openMailboxCommand;
    private readonly RelayCommand _toggleReadStateCommand;
    private readonly RelayCommand _toggleStarCommand;
    private readonly RelayCommand _archiveCommand;
    private readonly RelayCommand _deleteCommand;
    private readonly RelayCommand _clearFiltersCommand;

    private EmailMessage? _selectedMessage;
    private NavigationItem? _selectedNavigationItem;
    private string _searchText = string.Empty;
    private bool _showUnreadOnly;
    private bool _showStarredOnly;
    private bool _isLoading;
    private string _statusText = "Abre un archivo .mbox para empezar";
    private string _mailboxTitle = "Bandeja local";

    public MainViewModel()
    {
        Messages = new ObservableCollection<EmailMessage>();
        NavigationItems =
        [
            new NavigationItem { Key = "inbox", Icon = "I", Title = "Recibidos" },
            new NavigationItem { Key = "unread", Icon = "U", Title = "No leidos" },
            new NavigationItem { Key = "starred", Icon = "*", Title = "Destacados" },
            new NavigationItem { Key = "archive", Icon = "A", Title = "Archivados" },
            new NavigationItem { Key = "trash", Icon = "T", Title = "Papelera" },
            new NavigationItem { Key = "all", Icon = "#", Title = "Todos" }
        ];

        FilteredMessages = CollectionViewSource.GetDefaultView(Messages);
        FilteredMessages.Filter = FilterMessage;
        SelectedNavigationItem = NavigationItems[0];

        _openMailboxCommand = new RelayCommand(async () => await OpenMailboxAsync());
        _toggleReadStateCommand = new RelayCommand(_ => ToggleReadState(), _ => SelectedMessage is not null);
        _toggleStarCommand = new RelayCommand(_ => ToggleStar(), _ => SelectedMessage is not null);
        _archiveCommand = new RelayCommand(_ => ArchiveSelected(), _ => SelectedMessage is not null);
        _deleteCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedMessage is not null);
        _clearFiltersCommand = new RelayCommand(ClearFilters, () => ShowUnreadOnly || ShowStarredOnly || !string.IsNullOrWhiteSpace(SearchText));
    }

    public ObservableCollection<EmailMessage> Messages { get; }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public ICollectionView FilteredMessages { get; }

    public RelayCommand OpenMailboxCommand => _openMailboxCommand;

    public RelayCommand ToggleReadStateCommand => _toggleReadStateCommand;

    public RelayCommand ToggleStarCommand => _toggleStarCommand;

    public RelayCommand ArchiveCommand => _archiveCommand;

    public RelayCommand DeleteCommand => _deleteCommand;

    public RelayCommand ClearFiltersCommand => _clearFiltersCommand;

    public EmailMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            if (!SetProperty(ref _selectedMessage, value))
            {
                return;
            }

            if (_selectedMessage is not null && !_selectedMessage.IsRead)
            {
                _selectedMessage.IsRead = true;
            }

            RaisePropertyChanged(nameof(HasSelection));
            NotifyCommandStates();
            RefreshStats();
        }
    }

    public NavigationItem? SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            if (!SetProperty(ref _selectedNavigationItem, value))
            {
                return;
            }

            FilteredMessages.Refresh();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            FilteredMessages.Refresh();
            _clearFiltersCommand.NotifyCanExecuteChanged();
        }
    }

    public bool ShowUnreadOnly
    {
        get => _showUnreadOnly;
        set
        {
            if (!SetProperty(ref _showUnreadOnly, value))
            {
                return;
            }

            FilteredMessages.Refresh();
            _clearFiltersCommand.NotifyCanExecuteChanged();
        }
    }

    public bool ShowStarredOnly
    {
        get => _showStarredOnly;
        set
        {
            if (!SetProperty(ref _showStarredOnly, value))
            {
                return;
            }

            FilteredMessages.Refresh();
            _clearFiltersCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool HasSelection => SelectedMessage is not null;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string MailboxTitle
    {
        get => _mailboxTitle;
        set => SetProperty(ref _mailboxTitle, value);
    }

    private async Task OpenMailboxAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MBOX files (*.mbox)|*.mbox|All files (*.*)|*.*",
            Title = "Selecciona un archivo MBOX"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsLoading = true;
        StatusText = "Analizando archivo MBOX...";
        MailboxTitle = Path.GetFileName(dialog.FileName);

        try
        {
            foreach (var message in Messages)
            {
                message.PropertyChanged -= OnMessagePropertyChanged;
            }

            var parsedMessages = await _parser.ParseAsync(dialog.FileName);
            Messages.Clear();

            foreach (var message in parsedMessages.OrderByDescending(message => message.Date))
            {
                message.PropertyChanged += OnMessagePropertyChanged;
                Messages.Add(message);
            }

            SelectedMessage = Messages.FirstOrDefault();
            StatusText = $"{Messages.Count} mensajes cargados desde {Path.GetFileName(dialog.FileName)}";
            RefreshStats();
            FilteredMessages.Refresh();
        }
        catch (Exception exception)
        {
            StatusText = $"No se pudo abrir el archivo: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterMessage(object item)
    {
        if (item is not EmailMessage message)
        {
            return false;
        }

        if (!MatchesMailboxSection(message))
        {
            return false;
        }

        if (ShowUnreadOnly && message.IsRead)
        {
            return false;
        }

        if (ShowStarredOnly && !message.IsStarred)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return string.Join(' ', message.Subject, message.DisplaySender, message.Preview, message.PlainTextBody, string.Join(' ', message.Labels))
            .Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesMailboxSection(EmailMessage message)
    {
        return SelectedNavigationItem?.Key switch
        {
            "inbox" => !message.IsArchived && !message.IsDeleted,
            "unread" => !message.IsRead && !message.IsDeleted,
            "starred" => message.IsStarred && !message.IsDeleted,
            "archive" => message.IsArchived && !message.IsDeleted,
            "trash" => message.IsDeleted,
            "all" => true,
            _ => !message.IsDeleted
        };
    }

    private void ToggleReadState()
    {
        if (SelectedMessage is null)
        {
            return;
        }

        SelectedMessage.IsRead = !SelectedMessage.IsRead;
        StatusText = SelectedMessage.IsRead ? "Mensaje marcado como leido" : "Mensaje marcado como no leido";
        RefreshStats();
        FilteredMessages.Refresh();
    }

    private void ToggleStar()
    {
        if (SelectedMessage is null)
        {
            return;
        }

        SelectedMessage.IsStarred = !SelectedMessage.IsStarred;
        StatusText = SelectedMessage.IsStarred ? "Mensaje destacado" : "Destacado eliminado";
        RefreshStats();
        FilteredMessages.Refresh();
    }

    private void ArchiveSelected()
    {
        if (SelectedMessage is null)
        {
            return;
        }

        SelectedMessage.IsArchived = !SelectedMessage.IsArchived;
        SelectedMessage.IsDeleted = false;
        StatusText = SelectedMessage.IsArchived ? "Mensaje archivado" : "Mensaje devuelto a recibidos";
        RefreshStats();
        FilteredMessages.Refresh();
    }

    private void DeleteSelected()
    {
        if (SelectedMessage is null)
        {
            return;
        }

        SelectedMessage.IsDeleted = !SelectedMessage.IsDeleted;
        if (SelectedMessage.IsDeleted)
        {
            SelectedMessage.IsArchived = false;
        }

        StatusText = SelectedMessage.IsDeleted ? "Mensaje movido a la papelera" : "Mensaje restaurado";
        RefreshStats();
        FilteredMessages.Refresh();
    }

    private void ClearFilters()
    {
        SearchText = string.Empty;
        ShowUnreadOnly = false;
        ShowStarredOnly = false;
        FilteredMessages.Refresh();
    }

    private void RefreshStats()
    {
        foreach (var item in NavigationItems)
        {
            item.Count = item.Key switch
            {
                "inbox" => Messages.Count(message => !message.IsArchived && !message.IsDeleted),
                "unread" => Messages.Count(message => !message.IsRead && !message.IsDeleted),
                "starred" => Messages.Count(message => message.IsStarred && !message.IsDeleted),
                "archive" => Messages.Count(message => message.IsArchived && !message.IsDeleted),
                "trash" => Messages.Count(message => message.IsDeleted),
                "all" => Messages.Count,
                _ => 0
            };
        }

        RaisePropertyChanged(nameof(HasSelection));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        _toggleReadStateCommand.NotifyCanExecuteChanged();
        _toggleStarCommand.NotifyCanExecuteChanged();
        _archiveCommand.NotifyCanExecuteChanged();
        _deleteCommand.NotifyCanExecuteChanged();
        _clearFiltersCommand.NotifyCanExecuteChanged();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmailMessage.IsRead) or nameof(EmailMessage.IsStarred) or nameof(EmailMessage.IsArchived) or nameof(EmailMessage.IsDeleted))
        {
            RefreshStats();
            FilteredMessages.Refresh();
        }
    }
}

