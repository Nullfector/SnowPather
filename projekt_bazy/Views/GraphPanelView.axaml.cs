using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using projekt_bazy.Services;

namespace projekt_bazy.Views;

public partial class GraphPanelView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly string _graphName;

    private void PathFindingButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowPathFindingView(_graphName);
    }
    public GraphPanelView(MainWindow mainWindow, string graphName)
    {
        InitializeComponent();

        _mainWindow = mainWindow;
        _graphName = graphName;

        GraphNameTextBlock.Text = $"Aktywny: {_graphName}";
    }

    private async void DeleteGraphButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusTextBlock.Text = "Usuwanie miasta...";

            await using var service = new Neo4jService();

            await service.DeleteGraphAsync(_graphName);

            StatusTextBlock.Text = $"Usuniêto \"{_graphName}\".";

            var lines = File.ReadLines("graph_names.txt").Where(line => line.Trim() != _graphName.Trim()).ToList();
            File.WriteAllLines("graph_names.txt", lines);

            _mainWindow.ShowStartView();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"B³¹d usuwania: {ex.Message}";
        }
    }

    private void ShowVisualizerButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowGraphVisualizerView(_graphName);
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowStartView();
    }
}