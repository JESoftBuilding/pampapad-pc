using System.ComponentModel;

namespace PampaPad.App;

// El cierre con cambios sin guardar (01 §5, 05 §1). La página avisa con window.dirty y la cáscara pregunta con un diálogo nativo; no hay
// consulta de C# a la página. El diálogo sale de FormClosing (un clic en la X, Alt+F4 o Close() desde la bandeja), nunca de adentro de
// WebMessageReceived: un modal ahí es reentrancia que WebView2 no soporta (01 §9). Va aparte de EditorWindow.cs por el tope de 300 líneas.
internal sealed partial class EditorWindow
{
    /// <summary>
    /// Si la página tiene cambios sin guardar. Un Form no admite una propiedad pública con set sin estos dos atributos: el analizador
    /// WFO1000 lo marca, y con las advertencias como error no compila (medido el 2026-10-15).
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dirty { get; set; }

    /// <summary>window.dirty: la página avisa si tiene cambios sin guardar. Sólo desde el hilo de la interfaz. Composition lo llama con `?.`, que una asignación no admite.</summary>
    public void SetDirty(bool dirty) => Dirty = dirty;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Sólo si lo cierra una persona o la bandeja: apagar Windows o el Administrador de tareas no esperan una respuesta.
        if (Dirty && e.CloseReason is CloseReason.UserClosing or CloseReason.None)
        {
            var keep = new TaskDialogButton("Seguir editando");
            var close = new TaskDialogButton("Cerrar sin guardar");
            TaskDialogButton answer = TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = AppIdentity.Title,
                Heading = "Hay cambios sin guardar",
                Text = "Si cerrás la ventana ahora, se pierden.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { keep, close },
                DefaultButton = keep,
                AllowCancel = true,
            });
            if (answer != close)
            {
                e.Cancel = true;
                return;
            }

            _log.Info("window", "Ventana cerrada con cambios sin guardar, a pedido.");
        }

        base.OnFormClosing(e);
    }
}
