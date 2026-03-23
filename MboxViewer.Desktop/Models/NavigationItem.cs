using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MboxViewer.Desktop.Models;

public sealed class NavigationItem : INotifyPropertyChanged
{
    private int _count;

    public required string Key { get; init; }

    public required string Icon { get; init; }

    public required string Title { get; init; }

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}