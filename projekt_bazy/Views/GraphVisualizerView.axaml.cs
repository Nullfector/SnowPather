using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaGraphControl;
using projekt_bazy.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace projekt_bazy.Views;


public partial class GraphVisualizerView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly string _graphName;

    public GraphVisualizerView(MainWindow mainWindow, string graphName)
    {
        InitializeComponent();

        _mainWindow = mainWindow;
        _graphName = graphName;

        TitleTextBlock.Text = $"Wizualizacja miasta: {_graphName}";

        Loaded += async (_, _) => await LoadGraphAsync();
    }

    private async Task LoadGraphAsync()
    {
        try
        {
            TitleTextBlock.Text = $"£adowanie miasta: {_graphName}...";

            await using var service = new Neo4jService();

            int limit = int.TryParse(LimitTextBox.Text, out var parsed) ? parsed : 25;
            Graph graph = await service.GetGraphForVisualizationAsync(_graphName, limit);

            GraphPanel.Graph = graph;

            TitleTextBlock.Text =
                $"Wizualizacja: {_graphName} | Edges: {graph.Edges.Count}";
        }
        catch (Exception ex)
        {
            TitleTextBlock.Text = $"B³¹d ³adowania: {ex.Message}";
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadGraphAsync();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowGraphPanelView(_graphName);
    }
}