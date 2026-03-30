using Microsoft.AspNetCore.Components;
using BackgammonDiagram_Lib;

namespace BgQuiz_Blazor.Components.Pages;

public partial class Home : ComponentBase
{
    private string _clickMessage = "Click a point, the bar, cube, or tray.";

    // Hardcoded opening position for the test stub
    private readonly DiagramRequest _request = CreateOpeningPosition();
    private readonly DiagramOptions _options = new();

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

    /// <summary>
    /// Standard backgammon opening position.
    /// Adjust if DiagramRequest's API differs from this shape.
    /// </summary>
    private static DiagramRequest CreateOpeningPosition()
    {
        // TODO: Replace with actual DiagramRequest construction.
        // This assumes a constructor or factory that accepts point counts.
        // The opening position is:
        //   Player: 2 on 24, 5 on 13, 3 on 8, 5 on 6
        //   Opponent: 2 on 1, 5 on 12, 3 on 17, 5 on 19
        return new DiagramRequest();
    }
}
