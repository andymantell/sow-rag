using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace SoWImprover.Components.Shared;

/// <summary>
/// Wraps <see cref="InputFile"/> to handle a Blazor Server prerender race condition
/// where <c>blazor.web.js</c> has not finished initialising the internal
/// <c>InputFile</c> JS module by the time the interactive circuit renders the component.
/// This manifests as: <c>Cannot set properties of null (setting '_blazorInputFileNextFileId')</c>.
/// The wrapper catches the <see cref="JSException"/> and retries after a short delay.
/// </summary>
public class SafeInputFile : InputFile
{
    [Inject] private ILogger<SafeInputFile> Logger { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await base.OnAfterRenderAsync(firstRender);
        }
        catch (JSException ex) when (ex.Message.Contains("_blazorInputFileNextFileId"))
        {
            Logger.LogDebug("InputFile JS init race — retrying after delay");
            await Task.Delay(200);
            try
            {
                await base.OnAfterRenderAsync(firstRender);
            }
            catch (JSException)
            {
                // Circuit will recover on next user interaction
                Logger.LogWarning("InputFile JS init failed after retry — will recover on next render");
            }
        }
    }
}
