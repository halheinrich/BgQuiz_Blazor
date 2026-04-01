using Microsoft.AspNetCore.Components;
using BackgammonDiagram_Lib;

namespace BgQuiz_Blazor.Components.Pages;

public partial class Home : ComponentBase
{
    private string _clickMessage = "Click a point, the bar, cube, or tray.";

    private bool _onRollBearsOffRight = true;

    private DiagramRequest _request = new DiagramRequest.Builder
    {
        HomeBoardOnRight = true,
        Dice = [1, 1]
    }.Build();

    private readonly DiagramOptions _options = new();

    private void ToggleOrientation()
    {
        _onRollBearsOffRight = !_onRollBearsOffRight;
        _request = new DiagramRequest.Builder
        {
            HomeBoardOnRight = _onRollBearsOffRight,
            Dice = [1, 1] // TODO: replace with actual dice when CreateOpeningPosition() is implemented
        }.Build();
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