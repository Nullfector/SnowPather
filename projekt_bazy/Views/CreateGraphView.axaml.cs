using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using projekt_bazy.Services;

namespace projekt_bazy.Views;

public partial class CreateGraphView : UserControl
{
    private readonly MainWindow _mainWindow;
    private string? _selectedFilePath;

    public CreateGraphView(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
    }

    private async void ChooseFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is null)
        {
            StatusTextBlock.Text = "Nie uda³o siê otworzyæ okna wyboru pliku.";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Wybierz plik JSONL",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Pliki JSONL")
                    {
                        Patterns = ["*.jsonl"]
                    },
                    new FilePickerFileType("Wszystkie pliki")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            });

        var file = files.FirstOrDefault();

        if (file is null)
        {
            StatusTextBlock.Text = "Nie wybrano pliku.";
            return;
        }

        _selectedFilePath = file.TryGetLocalPath();

        if (_selectedFilePath is null)
        {
            StatusTextBlock.Text = "Nie uda³o siê pobraæ lokalnej œcie¿ki do pliku.";
            return;
        }

        FilePathTextBox.Text = _selectedFilePath;
        StatusTextBlock.Text = "Wybrano plik.";
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        var graphName = GraphNameTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(graphName))
        {
            StatusTextBlock.Text = "Podaj nazwê grafu.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedFilePath))
        {
            StatusTextBlock.Text = "Wybierz plik JSONL.";
            return;
        }

        try
        {
            ImportButton.IsEnabled = false;
            StatusTextBlock.Text = "Import trwa...";

            await using var service = new Neo4jService();

            //await service.TestConnectionAsync();
            var num = await service.GraphExistsAsync(graphName);
            if(num)
            {
                StatusTextBlock.Text = "Graf o takiej nazwie ju¿ istinieje!";
                return;
            }
            await service.ImportDataFromJSON(_selectedFilePath, graphName);

            StatusTextBlock.Text = $"Import grafu \"{graphName}\" zakoñczony poprawnie.";
            File.AppendAllText("graph_names.txt", graphName + "\n");



            // Opcjonalnie: po imporcie od razu przejdŸ do panelu grafu
            _mainWindow.ShowGraphPanelView(graphName);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"B³¹d importu: {ex.Message}";
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowStartView();
    }
}