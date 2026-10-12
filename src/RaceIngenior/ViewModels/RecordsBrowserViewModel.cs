using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaceIngenior.Models;
using RaceIngenior.Services;

namespace RaceIngenior.ViewModels;

public sealed class RecordTrackGroup
{
    public string TrackName { get; init; } = "";
    public ObservableCollection<RecordCarRow> Cars { get; } = new();
}

public sealed class RecordCarRow
{
    public string VehicleName { get; init; } = "";
    public string VehicleClass { get; init; } = "";
    public string BestLap { get; init; } = "";
    public string SetAt { get; init; } = "";
    public string ClassLine =>
        string.IsNullOrWhiteSpace(VehicleClass) ? "—" : VehicleClass.ToUpperInvariant();
}

public sealed partial class RecordsBrowserViewModel : ObservableObject
{
    private readonly TrackRecordsStore _store;
    private readonly SessionReportService _reports;

    public ObservableCollection<RecordTrackGroup> Tracks { get; } = new();

    [ObservableProperty] private string _statusText = "";

    public RecordsBrowserViewModel(TrackRecordsStore store, SessionReportService reports)
    {
        _store = store;
        _reports = reports;
        _store.RecordsChanged += () =>
            System.Windows.Application.Current?.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        // Re-merge reports so older sessions seed records after upgrades.
        _store.MergeFromReports(_reports.ListReports(), _reports.TryLoadReport);

        Tracks.Clear();
        var all = _store.GetAll();
        foreach (var trackGroup in all.GroupBy(r => r.TrackKey, StringComparer.OrdinalIgnoreCase))
        {
            var first = trackGroup.First();
            var group = new RecordTrackGroup
            {
                TrackName = string.IsNullOrWhiteSpace(first.TrackName)
                    ? first.TrackKey.ToUpperInvariant()
                    : first.TrackName.ToUpperInvariant()
            };

            foreach (var r in trackGroup.OrderBy(x => x.BestLapSec))
            {
                group.Cars.Add(new RecordCarRow
                {
                    VehicleName = string.IsNullOrWhiteSpace(r.VehicleName) ? "—" : r.VehicleName,
                    VehicleClass = r.VehicleClass,
                    BestLap = LapTimeFormat.Format(r.BestLapSec),
                    SetAt = r.SetAtUtc == default ? "—" : $"{r.SetAtUtc:yyyy-MM-dd HH:mm} UTC"
                });
            }

            Tracks.Add(group);
        }

        StatusText = all.Count == 0
            ? "No track records yet. Set a valid lap in a session to start the board."
            : $"{Tracks.Count} track{(Tracks.Count == 1 ? "" : "s")}  ·  {all.Count} car record{(all.Count == 1 ? "" : "s")}";
    }
}
