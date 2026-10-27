using Microsoft.Web.WebView2.Core;
using PampaPad.Core;

namespace PampaPad.App;

// Lo que llega de la página por WebMessageReceived. Va aparte de EditorWindow.cs por el tope de 300 líneas.
internal sealed partial class EditorWindow
{
    // async void porque es un evento. HandleAsync nunca tira (01 §4.1) y Send atrapa lo suyo; nada se espera bloqueando.
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOwnOrigin(e.Source))
        {
            _log.Warning("window", "Mensaje de un origen que no es el de la página, ignorado: " + e.Source);
            return;
        }

        // La página manda texto (realBridge.ts), por la misma razón que Send; TryGetWebMessageAsString tira si llega otra cosa.
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            _log.Warning("window", "Mensaje de la página que no es texto, ignorado.");
            return;
        }

        string reply = await _bridge.HandleAsync(json, Caller.Window, _closing.Token);
        Send(reply);
    }
}
