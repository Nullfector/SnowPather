using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using projekt_bazy.Services;

namespace projekt_bazy.Views;

public partial class StartView : UserControl
{
    private readonly MainWindow _mainWindow;
    //private List<string> _lista;

    private List<string> getNames(string path)
    {
        var lines = File.ReadAllLines(path).ToList();
        return lines;
    }

    public StartView(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        //_lista = getNames("seks");
        GraphNameTextBox.ItemsSource = getNames("graph_names.txt");
    }

    private async void ConnectGraphButton_Click(object? sender, RoutedEventArgs e)
    {
        var graphName = GraphNameTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(graphName))
        {
            StatusTextBlock.Text = "Podaj nazwê grafu.";
            return;
        }

        try
        {
            StatusTextBlock.Text = "Sprawdzanie grafu...";

            await using var service = new Neo4jService();

            bool exists = await service.GraphExistsAsync(graphName);

            if (!exists)
            {
                StatusTextBlock.Text = $"Graf \"{graphName}\" nie istnieje.";
                return;
            }

            _mainWindow.ShowGraphPanelView(graphName);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"B³¹d po³¹czenia: {ex.Message}";
        }
    }

    private void CreateNewGraphButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowCreateGraphView();
    }
}