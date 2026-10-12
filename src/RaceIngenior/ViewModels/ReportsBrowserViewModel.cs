using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaceIngenior.Models;
using RaceIngenior.Services;

namespace RaceIngenior.ViewModels;

public sealed partial class ReportsBrowserViewModel : ObservableObject
{
    private readonly SessionReportService _reports;

    public ObservableCollection<SessionReportSummary> Items { get; } = new();

    [ObservableProperty] private SessionReportSummary? _selected;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasSelection;

    public event Action<string, SessionReport>? OpenReportRequested;

    public ReportsBrowserViewModel(SessionReportService reports)
    {
        _reports = reports;
        Refresh();
    }

    partial void OnSelectedChanged(SessionReportSummary? value) =>
        HasSelection = value is not null;

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        var list = _reports.ListReports();
        foreach (var item in list)
            Items.Add(item);

        StatusText = list.Count == 0
            ? "No session reports yet. Finish a live session to generate one."
            : $"{list.Count} report{(list.Count == 1 ? "" : "s")}";

        if (Selected is not null)
        {
            Selected = Items.FirstOrDefault(i =>
                string.Equals(i.ReportId, Selected.ReportId, StringComparison.OrdinalIgnoreCase));
        }
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is null)
            return;

        var report = _reports.TryLoadReport(Selected.JsonPath);
        if (report is null)
        {
            StatusText = "Could not load report.";
            return;
        }

        var mdPath = Path.ChangeExtension(Selected.JsonPath, ".md") ?? Selected.JsonPath;
        OpenReportRequested?.Invoke(mdPath, report);
    }
}
