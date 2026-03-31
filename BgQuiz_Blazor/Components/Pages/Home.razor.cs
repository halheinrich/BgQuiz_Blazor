using Microsoft.AspNetCore.Components;
using BackgammonDiagram_Lib;

namespace BgQuiz_Blazor.Components.Pages;

public partial class Home : ComponentBase
{
    private string _clickMessage = "Click a point, the bar, cube, or tray.";

    private bool _flipped = false;

    private DiagramRequest _request = new DiagramRequest
    {
        Orientation = DiagramOrientation.OnRollRight
    };

    private readonly DiagramOptions _options = new();

    private void ToggleOrientation()
    {
        _flipped = !_flipped;
        _request = new DiagramRequest
        {
            Orientation = _flipped ? DiagramOrientation.OpponentRight : DiagramOrientation.OnRollRight
        };
    }

    private Task HandlePointClicked(int pointNumber)
    {
        _clickMessage = $"You clicked point {pointNumber}.";
        return Task.CompletedTask;
    }

    private Task HandleBarClicked()
    {
        _clickMessage = "You clicked the bar.";
        return Task.CompletedTask;
    }

    private Task HandleCubeClicked()
    {
        _clickMessage = "You clicked the cube.";
        return Task.CompletedTask;
    }

    private Task HandleTrayClicked()
    {
        _clickMessage = "You clicked the tray.";
        return Task.CompletedTask;
    }
}