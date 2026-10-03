using Microsoft.AspNetCore.Components;

namespace BgQuiz_Blazor.Client.Components;

/// <summary>
/// One of the action row tail's own buttons as the "⋯" list offers it
/// (<see cref="TailMenu"/>): the button's accessible name, what pressing it
/// does, and whether it is unavailable right now. The host builds each from
/// the very name, handler and gate its button renders from, so the item and
/// the button cannot differ (<c>SPEC-quiz-view.md</c> §4: "its list offers
/// each of them under the same names, and choosing one does what that control
/// does").
/// </summary>
/// <param name="Name">The button's accessible name, which the item shows and is named by.</param>
/// <param name="Choose">What the button does when pressed.</param>
/// <param name="Disabled">Whether the button is disabled now; the item is disabled with it.</param>
public sealed record TailMenuAction(string Name, EventCallback Choose, bool Disabled);
