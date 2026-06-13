using System.Windows;

namespace Asistente.Desktop
{
    /// <summary>
    /// Punto de entrada de la aplicación WPF.
    /// 
    /// Se hereda explícitamente de System.Windows.Application
    /// para evitar conflictos con System.Windows.Forms.Application,
    /// ya que el proyecto también usa Windows Forms para el icono
    /// de la bandeja del sistema.
    /// </summary>
    public partial class App : System.Windows.Application
    {
    }
}
