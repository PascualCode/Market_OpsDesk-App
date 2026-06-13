using Asistente.Desktop.Configuration;
using Asistente.Desktop.Infrastructure;
using Asistente.Desktop.Models;
using Asistente.Desktop.Services;
using Asistente.Desktop.DesktopTasks;

using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Media = System.Windows.Media;
using WinForms = System.Windows.Forms;
using Forms = System.Windows.Forms;

namespace Asistente.Desktop
{
    /// <summary>
    /// Ventana principal de la aplicación de escritorio.
    ///
    /// Esta ventana no se comporta como una ventana tradicional,
    /// sino como un avatar flotante en el escritorio.
    ///
    /// Responsabilidades principales:
    /// - Mostrar el avatar flotante.
    /// - Abrir/cerrar la burbuja de chat.
    /// - Enviar consultas a la API intermedia.
    /// - Recibir la respuesta en streaming.
    /// - Mostrar la respuesta progresivamente al usuario.
    /// - Mantener un historial breve de conversación.
    /// - Crear un icono en la bandeja del sistema.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Cliente HTTP reutilizable para comunicarse con la API intermedia.
        ///
        /// No se crea un HttpClient por petición porque eso puede provocar
        /// problemas de rendimiento y agotamiento de sockets.
        /// </summary>
        private readonly HttpClient _httpClient = new();

        /// <summary>
        /// Historial breve de conversación.
        ///
        /// Este historial se envía a la API para que el modelo tenga algo
        /// de contexto entre preguntas consecutivas.
        ///
        /// Ejemplo:
        /// Usuario: No me funciona Internet.
        /// Asistente: Revisa cable, IP, DNS...
        /// Usuario: ¿Cómo miro la IP?
        ///
        /// Gracias al historial, el asistente puede entender mejor que la
        /// segunda pregunta sigue relacionada con la incidencia de red.
        /// </summary>
        private readonly List<ChatHistoryMessage> _history = [];

        /// <summary>
        /// Configuración local de la aplicación.
        ///
        /// Se carga desde config.json, ubicado junto al ejecutable.
        /// Permite cambiar la URL de la API, nombre del asistente,
        /// modelo y tamaño de historial sin recompilar la aplicación.
        /// </summary>
        private AppConfig _config = new();

        /// <summary>
        /// Temporizador que comprueba periódicamente si la API está disponible.
        /// </summary>
        private readonly DispatcherTimer _healthTimer = new();

        /// <summary>
        /// Cliente centralizado para llamadas HTTP entre el Desktop y la API.
        ///
        /// Se inicializa en Window_Loaded, una vez cargado config.json.
        /// </summary>
        private AssistantApiClient? _assistantApiClient;

        ///********** SERVICIOS ****************

        /// <summary>
        /// Servicio dedicado al envío de mensajes
        /// y lectura de respuestas en streaming.
        /// </summary>
        private AssistantChatStreamService? _assistantChatStreamService;

        /// <summary>
        /// Servicio encargado de consultar y procesar
        /// recordatorios vencidos.
        /// </summary>
        private ReminderMonitorService? _reminderMonitorService;

        /// <summary>
        /// Servicio que ejecuta de forma segura las acciones
        /// locales permitidas en el equipo.
        /// </summary>
        private LocalActionExecutionService? _localActionExecutionService;

        /// <summary>
        /// Servicio que consulta y procesa acciones locales
        /// pendientes desde la API.
        /// </summary>
        private LocalActionMonitorService? _localActionMonitorService;

        /// <summary>
        /// Servicio que construye el contexto actual
        /// del usuario Windows y del equipo.
        /// </summary>
        private readonly DesktopClientContextService _desktopClientContextService = new();

        /// <summary>
        /// Servicio que valida archivos arrastrados
        /// a la Drop Zone de impresión rápida.
        /// </summary>
        private QuickPrintFileSelectionService? _quickPrintFileSelectionService;

        /// <summary>
        /// Archivos actualmente seleccionados para impresión rápida.
        /// </summary>
        private List<QuickPrintSelectedFile> _selectedQuickPrintFiles = [];

        /// <summary>
        /// Servicio que obtiene las impresoras disponibles
        /// del equipo para la impresión rápida.
        /// </summary>
        private QuickPrintPrinterService? _quickPrintPrinterService;

        /// <summary>
        /// Servicio específico de impresión PDF con Adobe Reader.
        /// </summary>
        private QuickPrintPdfPrintService? _quickPrintPdfPrintService;

        /// <summary>
        /// Servicio específico de impresión de imágenes.
        /// </summary>
        private QuickPrintImagePrintService? _quickPrintImagePrintService;

        /// <summary>
        /// Servicio orquestador que selecciona
        /// el motor de impresión según extensión.
        /// </summary>
        private QuickPrintDocumentPrintService? _quickPrintDocumentPrintService;

        /// <summary>
        /// Servicio específico de impresión de documentos Word.
        /// </summary>
        private QuickPrintWordPrintService? _quickPrintWordPrintService;

        /// <summary>
        /// Servicio específico de impresión de documentos Excel.
        /// </summary>
        private QuickPrintExcelPrintService? _quickPrintExcelPrintService;

        /// <summary>
        /// Servicio que solicita a la API la generación
        /// de carteles PDF.
        /// </summary>
        private PosterGeneratorService? _posterGeneratorService;

        /// <summary>
        /// Último cartel PDF generado desde el panel.
        /// </summary>
        private QuickPrintSelectedFile? _generatedPosterPdf;

        ///***** FIN DE SERVICIOS ******

        /// <summary>
        /// Resultados de la última búsqueda de productos.
        /// </summary>
        private List<PosterProductItem> _posterSearchResults = [];

        /// <summary>
        /// Etiquetas pendientes cargadas desde la API.
        /// </summary>
        private List<PendingLabelItem> _pendingLabels = [];

        /// <summary>
        /// Etiquetas seleccionadas para generar/imprimir.
        /// </summary>
        private List<PendingLabelItem> _selectedLabels = [];

        /// <summary>
        /// Productos añadidos al lote con tamaño y oferta propios.
        /// </summary>
        private List<PosterSelectedProductItem> _selectedPosterProducts = [];

        /// <summary>
        /// Indica si hay una consulta en curso.
        /// Sirve para evitar que el monitor de salud cambie el color mientras
        /// el asistente está pensando o respondiendo.
        /// </summary>
        private bool _isProcessingRequest = false;

        /// <summary>
        /// Icono de la bandeja del sistema.
        ///
        /// Permite mostrar, ocultar, limpiar la conversación o cerrar la app
        /// sin depender únicamente del avatar flotante.
        /// </summary>
        private WinForms.NotifyIcon? _trayIcon;

        /// <summary>
        /// Evita mostrar repetidamente el aviso de minimizado.
        /// </summary>
        private bool _minimizeTipShown = false;

        /// <summary>
        /// Token de cancelación de la petición actual.
        ///
        /// Nos permite cancelar una consulta anterior si el usuario lanza otra
        /// o si la aplicación se está cerrando.
        /// </summary>
        private CancellationTokenSource? _currentRequestCts;

        private DesktopTaskService? _desktopTaskService;

        private List<DesktopTaskItem> _desktopTasks = [];

        private List<DesktopTaskGroupItem> _desktopTaskGroups = [];

        private DesktopTaskItem? _currentTaskDetail;

        private bool _isEditingTaskDescription = false;

        /// <summary>
        /// Tareas después de aplicar filtros visuales.
        /// </summary>
        private List<DesktopTaskItem> _filteredDesktopTasks = [];

        /// <summary>
        /// Archivos seleccionados para adjuntar al crear una nueva tarea.
        /// </summary>
        private List<string> _newTaskAttachmentPaths = [];

        private T? FindControl<T>(
            string name)
            where T : class
        {
            return FindName(name) as T;
        }

        private System.Windows.Controls.ListBox? LabelPendingListBoxControl =>
            FindControl<System.Windows.Controls.ListBox>("LabelPendingListBox");

        private System.Windows.Controls.Grid? LabelPendingHeaderGridControl =>
            FindControl<System.Windows.Controls.Grid>("LabelPendingHeaderGrid");

        private System.Windows.Controls.Grid? LabelRestockSearchGridControl =>
            FindControl<System.Windows.Controls.Grid>("LabelRestockSearchGrid");

        private System.Windows.Controls.TextBox? LabelSearchTextBoxControl =>
            FindControl<System.Windows.Controls.TextBox>("LabelSearchTextBox");

        private System.Windows.Controls.Button? LabelSearchButtonControl =>
            FindControl<System.Windows.Controls.Button>("LabelSearchButton");

        /// <summary>
        /// Indica si el modo de etiquetas está en Reposición.
        /// </summary>
        private bool IsLabelRestockModeSelected()
        {
            var comboBox =
                FindName("LabelSourceComboBox") as System.Windows.Controls.ComboBox;

            if (comboBox is null)
                return false;

            var selectedText =
                (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Pendientes";

            return string.Equals(
                selectedText,
                "Reposición",
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Obtiene el formato de etiqueta seleccionado.
        /// </summary>
        private string GetSelectedLabelFormat()
        {
            var comboBox =
                FindName("LabelFormatComboBox") as System.Windows.Controls.ComboBox;

            if (comboBox is null)
                return "Normal";

            var selectedText =
                (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Normal";

            return selectedText switch
            {
                "Super reducido" => "SuperReducido",
                "Reducido" => "Reducido",
                _ => "Normal"
            };
        }

        /// <summary>
        /// Obtiene el tipo de búsqueda seleccionado para etiquetas de reposición.
        /// </summary>
        private string GetSelectedLabelSearchType()
        {
            var comboBox =
                FindName("LabelSearchTypeComboBox") as System.Windows.Controls.ComboBox;

            if (comboBox is null)
                return "Ean";

            var selectedText =
                (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "EAN";

            return selectedText switch
            {
                "Código artículo" => "ArticleCode",
                "Nombre" => "Name",
                _ => "Ean"
            };
        }

        /// <summary>
        /// Constructor principal de la ventana.
        /// InitializeComponent carga y enlaza los elementos definidos en XAML.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }


        private async void LabelSourceComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var pendingHeaderGrid =
                LabelPendingHeaderGridControl;

            var restockSearchGrid =
                LabelRestockSearchGridControl;

            var pendingListBox =
                LabelPendingListBoxControl;

            if (pendingHeaderGrid is null ||
                restockSearchGrid is null ||
                pendingListBox is null)
            {
                return;
            }

            var isRestock =
                IsLabelRestockModeSelected();

            pendingHeaderGrid.Visibility =
                isRestock
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            restockSearchGrid.Visibility =
                isRestock
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            pendingListBox.ItemsSource = null;
            _pendingLabels.Clear();

            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                isRestock
                    ? "Modo reposición activo. Busca etiquetas por EAN, código o nombre."
                    : "Modo pendientes activo. Carga etiquetas pendientes de impresión.";

            if (!isRestock)
            {
                await LoadPendingLabelsAsync();
            }
        }

        private async void LabelSearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var searchTextBox =
                LabelSearchTextBoxControl;

            var searchButton =
                LabelSearchButtonControl;

            var pendingListBox =
                LabelPendingListBoxControl;

            if (searchTextBox is null ||
                searchButton is null ||
                pendingListBox is null)
            {
                PosterStatusText.Text =
                    "No se han encontrado los controles de búsqueda de etiquetas.";

                return;
            }

            if (_posterGeneratorService is null)
            {
                PosterStatusText.Text =
                    "El servicio de etiquetas no está inicializado.";

                return;
            }

            var query =
                searchTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(query))
            {
                PosterStatusText.Text =
                    "Introduce un texto de búsqueda para reposición.";

                return;
            }

            searchButton.IsEnabled = false;

            PosterStatusText.Text =
                "Buscando etiquetas de reposición...";

            try
            {
                var searchType =
                    GetSelectedLabelSearchType();

                _pendingLabels =
                    await _posterGeneratorService.SearchLabelsAsync(
                        searchType,
                        query,
                        warehouse: "01",
                        maxResults: 100
                    );

                pendingListBox.ItemsSource = null;
                pendingListBox.ItemsSource = _pendingLabels;

                PosterStatusText.Text =
                    _pendingLabels.Count == 0
                        ? "No se han encontrado etiquetas de reposición."
                        : $"Etiquetas encontradas: {_pendingLabels.Count}.";
            }
            catch (Exception ex)
            {
                PosterStatusText.Text =
                    $"Error buscando etiquetas: {ex.Message}";
            }
            finally
            {
                searchButton.IsEnabled = true;
            }
        }

        private void LabelSearchTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;

            var searchButton =
                LabelSearchButtonControl;

            if (searchButton is null)
                return;

            LabelSearchButton_Click(
                searchButton,
                new RoutedEventArgs()
            );

            e.Handled = true;
        }


        /// <summary>
        /// Evento ejecutado cuando la ventana termina de cargarse.
        ///
        /// Aquí inicializamos la configuración, textos visuales, posición
        /// del avatar y el icono de la bandeja del sistema.
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Cargamos la configuración desde config.json.
            // Si no existe, se crea automáticamente con valores por defecto.
            _config = AppConfig.Load();

            _assistantApiClient = new AssistantApiClient(
                _httpClient,
                _config,
                _desktopClientContextService
            );

            _assistantChatStreamService = new AssistantChatStreamService(
                _httpClient,
                _config,
                _desktopClientContextService
            );

            _quickPrintFileSelectionService = new QuickPrintFileSelectionService(
                _config.QuickPrint
            );

            _quickPrintPrinterService = new QuickPrintPrinterService();

            _quickPrintPdfPrintService =
                new QuickPrintPdfPrintService(
                    _config.QuickPrint
                );

            _quickPrintImagePrintService =
                new QuickPrintImagePrintService();

            _quickPrintWordPrintService =
                new QuickPrintWordPrintService(
                    _quickPrintPdfPrintService
                );

            _quickPrintExcelPrintService =
                new QuickPrintExcelPrintService(
                    _quickPrintPdfPrintService
                );

            _quickPrintDocumentPrintService =
                new QuickPrintDocumentPrintService(
                    _quickPrintPdfPrintService,
                    _quickPrintImagePrintService,
                    _quickPrintWordPrintService,
                    _quickPrintExcelPrintService
                );

            _posterGeneratorService =
                new PosterGeneratorService(
                    _httpClient,
                    _config.PosterGenerator
                );

            _desktopTaskService =
                new DesktopTaskService(
                    _config.DesktopTasks,
                    _desktopClientContextService
                );

            _config.StartWithWindows = StartupManager.IsEnabled();
            _config.Save();

            // Personalizamos la interfaz con el nombre configurado.
            TitleText.Text = _config.AssistantName;
            SetAssistantStatus("unknown", "Comprobando conexión...");

            // Mensaje inicial visible cuando el usuario abre el chat.
            ResponseText.Text = "Hola, soy el Asistente . Haz clic en el avatar y dime en qué puedo ayudarte.";

            // Colocamos el avatar en la esquina inferior derecha de la pantalla.
            PositionBottomRight();

            // Creamos el icono de bandeja con menú contextual.
            CreateTrayIcon();

            StartHealthMonitor();

            _reminderMonitorService = new ReminderMonitorService(
                _assistantApiClient!,
                _config.ReminderPollingSeconds,
                ShowReminderNotificationAsync
            );

            _reminderMonitorService.Start();

            _localActionExecutionService = new LocalActionExecutionService(
                _config
            );

            _localActionMonitorService = new LocalActionMonitorService(
                _assistantApiClient!,
                _localActionExecutionService,
                _config.LocalActionsPollingSeconds
            );

            _localActionMonitorService.Start();

            await _reminderMonitorService.CheckNowAsync();

            await CheckApiHealthAsync();
        }

        /// <summary>
        /// Evento ejecutado cuando la ventana se cierra.
        ///
        /// Es importante liberar recursos:
        /// - Cancelar petición activa.
        /// - Eliminar icono de bandeja.
        /// - Liberar HttpClient.
        /// </summary>
        private void Window_Closed(object? sender, EventArgs e)
        {
            _currentRequestCts?.Cancel();
            _reminderMonitorService?.Dispose();
            _localActionMonitorService?.Dispose();
            _trayIcon?.Dispose();
            _httpClient.Dispose();
        }

        /// <summary>
        /// Posiciona la ventana completa en la esquina inferior derecha
        /// del área de trabajo de Windows.
        ///
        /// SystemParameters.WorkArea evita colocar la app encima de la barra
        /// de tareas.
        /// </summary>
        private void PositionBottomRight()
        {
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Bottom - Height - 20;
        }

        /// <summary>
        /// Carga el icono corporativo desde Assets/app.ico.
        /// Si no existe, usa el icono genérico de Windows.
        /// </summary>
        private Drawing.Icon LoadTrayIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "avatar.ico");

            if (File.Exists(iconPath))
            {
                return new Drawing.Icon(iconPath);
            }

            return Drawing.SystemIcons.Application;
        }

        /// <summary>
        /// Muestra información básica de la aplicación.
        /// </summary>
        private void ShowAbout()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            var message =
                $"{_config.AssistantName}\n\n" +
                $"Versión: {version}\n" +
                $"API configurada:\n{_config.ApiStreamUrl}\n\n" +
                $"Estado actual: {StatusText.Text}\n\n" +
                "Cliente de escritorio flotante conectado al asistente IA local de .";

            System.Windows.MessageBox.Show(
                this,
                message,
                $"Acerca de {_config.AssistantName}",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        /// <summary>
        /// Crea el icono de la bandeja del sistema.
        ///
        /// Opciones incluidas:
        /// - Mostrar / ocultar chat.
        /// - Limpiar conversación.
        /// - Salir de la aplicación.
        ///
        /// En una versión futura se puede sustituir SystemIcons.Application
        /// por un icono corporativo .ico.
        /// </summary>
        private void CreateTrayIcon()
        {
            var menu = new WinForms.ContextMenuStrip();

            menu.Items.Add("Mostrar / ocultar", null, (_, _) =>
            {
                Dispatcher.Invoke(ToggleChat);
            });

            menu.Items.Add("Limpiar conversación", null, (_, _) =>
            {
                Dispatcher.Invoke(ClearConversation);
            });

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var startWithWindowsItem = new WinForms.ToolStripMenuItem("Iniciar con Windows")
            {
                CheckOnClick = true,
                Checked = StartupManager.IsEnabled()
            };

            startWithWindowsItem.CheckedChanged += (_, _) =>
            {
                var enabled = startWithWindowsItem.Checked;

                StartupManager.SetEnabled(enabled);

                _config.StartWithWindows = enabled;
                _config.Save();

                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = enabled
                        ? "Inicio con Windows activado."
                        : "Inicio con Windows desactivado.";
                });
            };

            menu.Items.Add(startWithWindowsItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            menu.Items.Add("Acerca de", null, (_, _) =>
            {
                Dispatcher.Invoke(ShowAbout);
            });

            menu.Items.Add(new WinForms.ToolStripSeparator());

            menu.Items.Add("Salir", null, (_, _) =>
            {
                Dispatcher.Invoke(Close);
            });

            _trayIcon = new WinForms.NotifyIcon
            {
                Text = _config.AssistantName,
                Icon = LoadTrayIcon(),
                Visible = true,
                ContextMenuStrip = menu
            };

            _trayIcon.DoubleClick += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Show();
                    Activate();
                    //ToggleChat();
                });
            };
        }

        /// <summary>
        /// Muestra el panel de chat con animación suave.
        /// </summary>
        private void ShowChatAnimated()
        {
            ChatPanel.Visibility = Visibility.Visible;
            ChatPanel.Opacity = 0;
            ChatPanelScale.ScaleX = 0.92;
            ChatPanelScale.ScaleY = 0.92;

            var storyboard = ((Storyboard)FindResource("OpenChatStoryboard")).Clone();
            storyboard.Begin(this);

            QuestionBox.Focus();
        }

        /// <summary>
        /// Oculta el panel de chat con animación suave.
        /// </summary>
        private void HideChatAnimated(
            bool showMinimizeTip = true)
        {
            var storyboard =
                ((Storyboard)FindResource("CloseChatStoryboard")).Clone();

            storyboard.Completed += (_, _) =>
            {
                ChatPanel.Visibility = Visibility.Collapsed;

                if (showMinimizeTip && !_minimizeTipShown)
                {
                    _trayIcon?.ShowBalloonTip(
                        2200,
                        _config.AssistantName,
                        "El asistente sigue disponible desde el avatar o la bandeja del sistema.",
                        WinForms.ToolTipIcon.Info
                    );

                    _minimizeTipShown = true;
                }
            };

            storyboard.Begin(this);
        }

        /// <summary>
        /// Alterna la visibilidad del panel de chat usando animación.
        /// </summary>
        private void ToggleChat()
        {
            if ( ChatPanel.Visibility == Visibility.Visible )
            {
                HideChatAnimated();
                return;
            }

            if (QuickPrintPanel.Visibility == Visibility.Visible)
            {
                HideQuickPrintPanelAnimated();
            }

            if (PosterPanel.Visibility == Visibility.Visible)
            {
                HidePosterPanelAnimated();
            }

            if (TasksPanel.Visibility == Visibility.Visible)
            {
                HideTasksPanel();
            }

            ShowChatAnimated();
        }

        /// <summary>
        /// Muestra el panel de impresión rápida con animación.
        /// </summary>
        private void ShowQuickPrintPanelAnimated()
        {
            LoadQuickPrintPrinters();

            QuickPrintPanel.Visibility = Visibility.Visible;
            QuickPrintPanel.Opacity = 0;
            QuickPrintPanelScale.ScaleX = 0.92;
            QuickPrintPanelScale.ScaleY = 0.92;

            var storyboard =
                ((Storyboard)FindResource("OpenQuickPrintStoryboard")).Clone();

            storyboard.Begin(this);
        }

        /// <summary>
        /// Oculta el panel de impresión rápida con animación.
        /// </summary>
        private void HideQuickPrintPanelAnimated()
        {
            var storyboard =
                ((Storyboard)FindResource("CloseQuickPrintStoryboard")).Clone();

            storyboard.Completed += (_, _) =>
            {
                QuickPrintPanel.Visibility = Visibility.Collapsed;

                SetQuickPrintDropZoneVisualState("normal");
            };

            storyboard.Begin(this);
        }

        /// <summary>
        /// Alterna la visibilidad del panel de impresión rápida.
        /// </summary>
        private void ToggleQuickPrintPanel()
        {
            if (QuickPrintPanel.Visibility == Visibility.Visible)
            {
                HideQuickPrintPanelAnimated();
                return;
            }

            if (ChatPanel.Visibility == Visibility.Visible)
            {
                HideChatAnimated(showMinimizeTip: false);
            }

            if (PosterPanel.Visibility == Visibility.Visible)
            {
                HidePosterPanelAnimated();
            }

            if (TasksPanel.Visibility == Visibility.Visible)
            {
                HideTasksPanel();
            }

            ShowQuickPrintPanelAnimated();
        }

        /// <summary>
        /// Muestra el panel de generación de carteles.
        /// </summary>
        private void ShowPosterPanelAnimated()
        {
            LoadPosterPrinters();

            PosterPanel.Visibility = Visibility.Visible;
            PosterPanel.Opacity = 0;
            PosterPanelScale.ScaleX = 0.92;
            PosterPanelScale.ScaleY = 0.92;

            var storyboard =
                ((Storyboard)FindResource("OpenPosterPanelStoryboard")).Clone();

            storyboard.Begin(this);
        }

        /// <summary>
        /// Oculta el panel de generación de carteles.
        /// </summary>
        private void HidePosterPanelAnimated()
        {
            var storyboard =
                ((Storyboard)FindResource("ClosePosterPanelStoryboard")).Clone();

            storyboard.Completed += (_, _) =>
            {
                PosterPanel.Visibility = Visibility.Collapsed;
            };

            storyboard.Begin(this);
        }

        /// <summary>
        /// Alterna la visibilidad del panel de carteles.
        /// </summary>
        private void TogglePosterPanel()
        {
            if (PosterPanel.Visibility == Visibility.Visible)
            {
                HidePosterPanelAnimated();
                return;
            }

            if (ChatPanel.Visibility == Visibility.Visible)
            {
                HideChatAnimated(showMinimizeTip: false);
            }

            if (QuickPrintPanel.Visibility == Visibility.Visible)
            {
                HideQuickPrintPanelAnimated();
            }

            if (TasksPanel.Visibility == Visibility.Visible)
            {
                HideTasksPanel();
            }

            ShowPosterPanelAnimated();
        }

        /// <summary>
        /// Evento de clic izquierdo sobre el avatar.
        ///
        /// Comportamiento:
        /// - Clic normal: abre/cierra el chat.
        /// - CTRL + clic: permite arrastrar el avatar por la pantalla.
        ///
        /// Esto permite mantener el avatar flotante y recolocarlo si molesta.
        /// </summary>
        private void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                DragMove();
                return;
            }

            ToggleChat();
        }

        /// <summary>
        /// Oculta el panel de chat al pulsar el botón X.
        ///
        /// No cierra la aplicación, solo esconde la burbuja.
        /// </summary>
        private void HideChat_Click(object sender, RoutedEventArgs e)
        {
            HideChatAnimated();
        }

        /// <summary>
        /// Cancela la respuesta actual del asistente.
        /// 
        /// Se usa cuando el modelo está generando una respuesta demasiado larga
        /// o el usuario ya no necesita continuar esperando.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRequestCts is null)
                return;

            StatusText.Text = "Cancelando respuesta...";
            CancelButton.IsEnabled = false;

            _currentRequestCts.Cancel();
        }

        /// <summary>
        /// Botón Limpiar.
        /// Borra la conversación actual y el historial enviado al modelo.
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearConversation();
        }

        /// <summary>
        /// Botón flotante que abre/cierra la impresión rápida.
        /// </summary>
        private void QuickPrintToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ToggleQuickPrintPanel();
        }

        /// <summary>
        /// Botón X del panel de impresión rápida.
        /// </summary>
        private void HideQuickPrintPanel_Click(
            object sender,
            RoutedEventArgs e)
        {
            HideQuickPrintPanelAnimated();
        }

        /// <summary>
        /// Botón flotante del generador de carteles.
        /// </summary>
        private void PosterToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TogglePosterPanel();
        }

        /// <summary>
        /// Botón X del panel de carteles.
        /// </summary>
        private void HidePosterPanel_Click(
            object sender,
            RoutedEventArgs e)
        {
            HidePosterPanelAnimated();
        }

        /// <summary>
        /// Genera el PDF de carteles a partir de los artículos añadidos al lote.
        ///
        /// Cada artículo del lote puede tener:
        /// - tamaño propio,
        /// - tipo de oferta propio,
        /// - y puede repetirse varias veces.
        /// </summary>
        private async void PosterGenerateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_posterGeneratorService is null)
            {
                PosterStatusText.Text =
                    "El servicio de generación no está inicializado.";

                PosterPrintButton.IsEnabled = false;
                PosterPreviewButton.IsEnabled = false;
                return;
            }

            if (IsLabelModeSelected())
            {
                await GenerateLabelsFromDesktopAsync();
                return;
            }

            if (_selectedPosterProducts.Count == 0)
            {
                PosterStatusText.Text =
                    "Añade al menos un producto al lote antes de generar el PDF.";

                PosterPrintButton.IsEnabled = false;
                PosterPreviewButton.IsEnabled = false;
                return;
            }

            PosterGenerateButton.IsEnabled = false;
            PosterPrintButton.IsEnabled = false;
            PosterPreviewButton.IsEnabled = false;

            _generatedPosterPdf = null;

            PosterStatusText.Text =
                $"Generando PDF mixto. Artículos: {_selectedPosterProducts.Count}...";

            var result =
                await _posterGeneratorService.GeneratePosterBatchAsync(
                    _selectedPosterProducts
                );

            PosterStatusText.Text =
                result.Message;

            if (!result.Success || result.GeneratedPdf is null)
            {
                PosterPrintButton.IsEnabled = false;
                PosterPreviewButton.IsEnabled = false;
                PosterGenerateButton.IsEnabled = true;
                return;
            }

            _generatedPosterPdf =
                result.GeneratedPdf;

            PosterPreviewButton.IsEnabled = true;

            PosterPrintButton.IsEnabled =
                PosterPrinterComboBox.SelectedItem is QuickPrintPrinterItem;

            PosterGenerateButton.IsEnabled = true;
        }

        /// <summary>
        /// Imprime el último cartel PDF generado usando
        /// la impresora seleccionada en el panel.
        /// </summary>
        private async void PosterPrintButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_quickPrintPdfPrintService is null)
            {
                PosterStatusText.Text =
                    "El servicio de impresión PDF no está inicializado.";

                return;
            }

            if (_generatedPosterPdf is null)
            {
                PosterStatusText.Text =
                    IsLabelModeSelected()
                        ? "Primero genera un PDF de etiquetas válido."
                        : "Primero genera un PDF de cartel válido.";

                PosterPrintButton.IsEnabled = false;
                return;
            }

            if (PosterPrinterComboBox.SelectedItem
                is not QuickPrintPrinterItem selectedPrinter)
            {
                PosterStatusText.Text =
                    "Selecciona una impresora válida.";

                return;
            }

            PosterPrintButton.IsEnabled = false;

            PosterStatusText.Text =
                IsLabelModeSelected()
                    ? $"Enviando etiquetas a impresión: {_generatedPosterPdf.FileName}"
                    : $"Enviando cartel a impresión: {_generatedPosterPdf.FileName}";

            var printResult =
                _quickPrintPdfPrintService.PrintPdf(
                    _generatedPosterPdf,
                    selectedPrinter
                );

            PosterStatusText.Text =
                printResult.Message;

            if (!printResult.Success)
            {
                PosterPrintButton.IsEnabled =
                    _generatedPosterPdf is not null;

                return;
            }

            /*
             * Marcado como impreso:
             * Solo se ejecuta si:
             * - estamos en modo Etiqueta,
             * - NO estamos en modo Reposición,
             * - el checkbox está marcado,
             * - hay etiquetas seleccionadas.
             *
             * Importante:
             * De momento NO marcamos como impresas las etiquetas de reposición,
             * porque no son etiquetas pendientes de impresión.
             */
            if (IsLabelModeSelected() &&
                !IsLabelRestockModeSelected() &&
                ShouldMarkLabelsAsPrinted() &&
                _selectedLabels.Count > 0)
            {
                if (_posterGeneratorService is null)
                {
                    PosterStatusText.Text =
                        "La impresión se ha enviado, pero no se pudo marcar como impreso porque el servicio no está inicializado.";

                    PosterPrintButton.IsEnabled =
                        _generatedPosterPdf is not null;

                    return;
                }

                PosterStatusText.Text =
                    "Impresión enviada. Marcando etiquetas como impresas...";

                try
                {
                    var markResult =
                        await _posterGeneratorService.MarkLabelsAsPrintedAsync(
                            _selectedLabels
                        );

                    PosterStatusText.Text =
                        markResult.Message
                        ?? $"Etiquetas marcadas como impresas: {markResult.UpdatedCount}.";

                    if (markResult.Success)
                    {
                        _selectedLabels.Clear();

                        RefreshSelectedLabels();

                        await LoadPendingLabelsAsync();

                        InvalidateGeneratedPosterPdf();
                    }
                }
                catch (Exception ex)
                {
                    PosterStatusText.Text =
                        $"La impresión se ha enviado, pero hubo un error marcando como impreso: {ex.Message}";
                }
            }

            PosterPrintButton.IsEnabled =
                _generatedPosterPdf is not null;
        }

        /// <summary>
        /// Indica si el usuario quiere marcar las etiquetas como impresas
        /// después de enviar la impresión.
        /// </summary>
        private bool ShouldMarkLabelsAsPrinted()
        {
            var checkBox =
                FindName("LabelMarkAsPrintedCheckBox")
                    as System.Windows.Controls.CheckBox;

            return checkBox?.IsChecked == true;
        }

        /// <summary>
        /// Permite generar el cartel pulsando Enter
        /// desde el campo de código.
        /// </summary>
        private void PosterProductCodeTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            PosterGenerateButton_Click(
                PosterGenerateButton,
                new RoutedEventArgs()
            );

            e.Handled = true;
        }

        /// <summary>
        /// Limpia historial y reinicia el texto visible del asistente.
        ///
        /// Esto es útil cuando el usuario quiere empezar una incidencia nueva
        /// sin que el modelo tenga en cuenta la conversación anterior.
        /// </summary>
        private void ClearConversation()
        {
            _history.Clear();
            ResponseText.Text = "Conversación limpiada. ¿En qué puedo ayudarte?";
            StatusText.Text = "Listo.";
            QuestionBox.Focus();
        }

        /// <summary>
        /// Evento del botón Enviar.
        /// Lanza el envío de la consulta al asistente.
        /// </summary>
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendQuestionAsync();
        }

        /// <summary>
        /// Evento de teclado en la caja de texto.
        ///
        /// Si el usuario pulsa Enter, se envía la pregunta.
        /// En una versión futura podríamos permitir Shift+Enter para saltos
        /// de línea si la caja pasa a ser multilinea.
        /// </summary>
        private async void QuestionBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await SendQuestionAsync();
            }
        }

        /// <summary>
        /// Inicia el monitor periódico de salud de la API.
        /// Cada cierto tiempo consulta /health para saber si la API está disponible.
        /// </summary>
        private void StartHealthMonitor()
        {
            _healthTimer.Interval = TimeSpan.FromSeconds(15);

            _healthTimer.Tick += async (_, _) =>
            {
                await CheckApiHealthAsync();
            };

            _healthTimer.Start();
        }

        /// <summary>
        /// Efecto visual al pasar el ratón por encima del avatar.
        /// </summary>
        private void Avatar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateAvatarScale(1.06);
        }

        /// <summary>
        /// Devuelve el avatar a su tamaño normal al retirar el ratón.
        /// </summary>
        private void Avatar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateAvatarScale(1.0);
        }

        /// <summary>
        /// Anima suavemente el tamaño del avatar.
        /// </summary>
        private void AnimateAvatarScale(double scale)
        {
            var animation = new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            AvatarScale.BeginAnimation(Media.ScaleTransform.ScaleXProperty, animation);
            AvatarScale.BeginAnimation(Media.ScaleTransform.ScaleYProperty, animation);
        }

        /// <summary>
        /// Oculta la ventana principal sin cerrar la aplicación.
        /// La app seguirá disponible desde el icono de la bandeja del sistema.
        /// </summary>
        private void FloatingHideButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (ChatPanel.Visibility == Visibility.Visible)
            {
                HideChatAnimated(showMinimizeTip: false);
            }

            if (QuickPrintPanel.Visibility == Visibility.Visible)
            {
                HideQuickPrintPanelAnimated();
            }

            if (PosterPanel.Visibility == Visibility.Visible)
            {
                HidePosterPanelAnimated();
            }

            if (TasksPanel.Visibility == Visibility.Visible)
            {
                HideTasksPanel();
            }

            Hide();
        }

        /// <summary>
        /// Comprueba si la API intermedia responde correctamente.
        ///
        /// La llamada HTTP real se delega en AssistantApiClient.
        /// MainWindow solo traduce el resultado a estado visual.
        /// </summary>
        private async Task CheckApiHealthAsync()
        {
            if (_isProcessingRequest)
                return;

            if (_assistantApiClient is null)
                return;

            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(3)
            );

            var result = await _assistantApiClient.CheckHealthAsync(
                cts.Token
            );

            if (result.IsAvailable)
            {
                SetAssistantStatus("ready", "Listo para ayudarte.");
            }
            else if (result.StatusCode.HasValue)
            {
                SetAssistantStatus(
                    "error",
                    $"API no disponible ({result.StatusCode.Value})."
                );
            }
            else
            {
                SetAssistantStatus("error", "API no disponible.");
            }
        }

        /// <summary>
        /// Método principal de envío de consulta.
        ///
        /// Flujo:
        /// 1. Lee la pregunta del usuario.
        /// 2. Construye el JSON de petición.
        /// 3. Envía la petición a la API intermedia.
        /// 4. Lee la respuesta en streaming.
        /// 5. Muestra el texto progresivamente.
        /// 6. Guarda pregunta y respuesta en historial.
        /// </summary>
        private async Task SendQuestionAsync()
        {
            var question = QuestionBox.Text.Trim();

            // Evitamos enviar preguntas vacías.
            if (string.IsNullOrWhiteSpace(question))
                return;

            // Limpiamos la caja y preparamos la interfaz para la respuesta.
            QuestionBox.Clear();
            ResponseText.Text = "";
            SetAssistantStatus("thinking", "Pensando...");
            _isProcessingRequest = true;
            SendButton.IsEnabled = false;
            QuestionBox.IsEnabled = false;
            CancelButton.IsEnabled = true;

            // Cancelamos cualquier petición anterior que siguiera activa.
            _currentRequestCts?.Cancel();
            _currentRequestCts = new CancellationTokenSource();

            try
            {
                if (_assistantChatStreamService is null)
                {
                    ResponseText.Text =
                        "El servicio de conversación no está inicializado.";

                    SetAssistantStatus("error", "Servicio no disponible.");
                    return;
                }

                var streamResult =
                    await _assistantChatStreamService.StreamResponseAsync(
                        question,
                        _history
                            .TakeLast(_config.MaxHistoryMessages)
                            .ToList(),
                        onStreamStarted: () =>
                        {
                            SetAssistantStatus("responding", "Respondiendo...");
                        },
                        onChunkReceived: async chunk =>
                        {
                            ResponseText.Text += chunk;
                            ResponseScroll.ScrollToEnd();

                            // Cedemos control al hilo visual
                            // para que la interfaz refresque progresivamente.
                            await Dispatcher.InvokeAsync(() => { });
                        },
                        cancellationToken: _currentRequestCts.Token
                    );

                if (!streamResult.Success)
                {
                    ResponseText.Text =
                        streamResult.ErrorMessage
                        ?? "Error consultando al asistente.";

                    SetAssistantStatus("error", "Error de conexión.");
                    return;
                }

                var answer = streamResult.Answer;

                if (!string.IsNullOrWhiteSpace(answer))
                {
                    AddToHistory("user", question);
                    AddToHistory("assistant", answer);
                }

                SetAssistantStatus("ready", "Listo para ayudarte.");
            }
            catch (TaskCanceledException)
            {
                SetAssistantStatus("ready", "Respuesta cancelada.");

                if (string.IsNullOrWhiteSpace(ResponseText.Text))
                {
                    ResponseText.Text = "La respuesta fue cancelada antes de recibir contenido.";
                }
            }
            catch (Exception ex)
            {
                // Cualquier fallo de red, API caída, servidor no disponible,
                // error de DNS, etc., se muestra al usuario de forma clara.
                ResponseText.Text =
                    $"No se pudo conectar con el asistente.\n\nDetalle: {ex.Message}";
                SetAssistantStatus("error", "Error de conexión.");
            }
            finally
            {
                // Rehabilitamos controles pase lo que pase.
                _isProcessingRequest = false;
                SendButton.IsEnabled = true;
                QuestionBox.IsEnabled = true;
                CancelButton.IsEnabled = false;
                QuestionBox.Focus();
            }
        }

        /// <summary>
        /// Cambia el estado visual del asistente.
        ///
        /// ready      -> verde
        /// thinking   -> amarillo
        /// responding -> azul
        /// error      -> rojo
        /// unknown    -> gris
        ///
        /// Ahora el estado afecta a:
        /// - Punto pequeño de cabecera.
        /// - Borde del avatar.
        /// - Fondo suave del avatar.
        /// - Texto de estado.
        /// </summary>
        private void SetAssistantStatus(string status, string message)
        {
            Media.Brush mainColor = status switch
            {
                "ready" => new Media.SolidColorBrush(Media.Color.FromRgb(34, 197, 94)),
                "thinking" => new Media.SolidColorBrush(Media.Color.FromRgb(234, 179, 8)),
                "responding" => new Media.SolidColorBrush(Media.Color.FromRgb(37, 99, 235)),
                "error" => new Media.SolidColorBrush(Media.Color.FromRgb(239, 68, 68)),
                _ => new Media.SolidColorBrush(Media.Color.FromRgb(156, 163, 175))
            };

            Media.Brush softColor = status switch
            {
                "ready" => new Media.SolidColorBrush(Media.Color.FromRgb(240, 253, 244)),
                "thinking" => new Media.SolidColorBrush(Media.Color.FromRgb(254, 252, 232)),
                "responding" => new Media.SolidColorBrush(Media.Color.FromRgb(239, 246, 255)),
                "error" => new Media.SolidColorBrush(Media.Color.FromRgb(254, 242, 242)),
                _ => new Media.SolidColorBrush(Media.Color.FromRgb(243, 244, 246))
            };

            StatusIndicator.Fill = mainColor;
            AvatarBorder.BorderBrush = mainColor;
            AvatarGlow.Fill = softColor;
            StatusText.Text = message;
        }

        /// <summary>
        /// Añade un mensaje al historial local.
        ///
        /// El historial se limita para no enviar conversaciones enormes,
        /// lo cual mejora rendimiento y evita consumir demasiado contexto.
        /// </summary>
        private void AddToHistory(string role, string content)
        {
            _history.Add(new ChatHistoryMessage
            {
                Role = role,
                Content = content
            });

            var max = Math.Max(_config.MaxHistoryMessages, 2);

            while (_history.Count > max)
            {
                _history.RemoveAt(0);
            }
        }

        /// <summary>
        /// Muestra al usuario un aviso visual desde el icono de bandeja
        /// cuando un recordatorio ya ha vencido.
        /// </summary>
        private Task ShowReminderNotificationAsync(
            DueReminderDesktopItem reminder)
        {
            if (_trayIcon is null)
                return Task.CompletedTask;

            var title = $"{_config.AssistantName} - Recordatorio";

            var dateText = reminder.RemindAtUtc
                .ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm");

            var message =
                $"{reminder.Title}\n" +
                $"Programado para: {dateText}";

            if (!string.IsNullOrWhiteSpace(reminder.Notes))
            {
                message += $"\n{reminder.Notes}";
            }

            _trayIcon.ShowBalloonTip(
                15000,
                title,
                message,
                WinForms.ToolTipIcon.Info
            );

            return Task.CompletedTask;
        }

        /// <summary>
        /// El usuario ha arrastrado algo dentro de la Drop Zone.
        /// Cambiamos el aspecto visual si contiene archivos.
        /// </summary>
        private void QuickPrintDropZone_DragEnter(
            object sender,
            System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                SetQuickPrintDropZoneVisualState("active");
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
                SetQuickPrintDropZoneVisualState("error");
            }

            e.Handled = true;
        }

        /// <summary>
        /// Mantiene el efecto de copia durante el arrastre.
        /// </summary>
        private void QuickPrintDropZone_DragOver(
            object sender,
            System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }

            e.Handled = true;
        }

        /// <summary>
        /// El usuario abandona la Drop Zone sin soltar archivo.
        /// Restauramos el aspecto normal.
        /// </summary>
        private void QuickPrintDropZone_DragLeave(
            object sender,
            System.Windows.DragEventArgs e)
        {
            SetQuickPrintDropZoneVisualState("normal");
        }

        /// <summary>
        /// El usuario suelta uno o más archivos sobre la Drop Zone.
        /// Validamos los ficheros y actualizamos la UI.
        /// </summary>
        private void QuickPrintDropZone_Drop(
            object sender,
            System.Windows.DragEventArgs e)
        {
            SetQuickPrintDropZoneVisualState("normal");

            if (_quickPrintFileSelectionService is null)
            {
                QuickPrintSelectedFileText.Text =
                    "El módulo de impresión rápida no está inicializado.";

                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                QuickPrintSelectedFileText.Text =
                    "Solo se permiten archivos arrastrados desde Windows.";

                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            var filePaths =
                e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];

            var isValid =
                _quickPrintFileSelectionService.TrySelectFiles(
                    filePaths,
                    out var selectedFiles,
                    out var message
                );

            QuickPrintSelectedFileText.Text = message;

            if (!isValid || selectedFiles.Count == 0)
            {
                _selectedQuickPrintFiles = [];
                UpdateQuickPrintPrintButtonState();
                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            _selectedQuickPrintFiles = selectedFiles;

            UpdateQuickPrintPrintButtonState();

            SetQuickPrintDropZoneVisualState("success");
        }

        /// <summary>
        /// Cambia la apariencia visual de la zona de arrastre
        /// según el estado actual.
        /// </summary>
        private void SetQuickPrintDropZoneVisualState(
            string state)
        {
            switch (state)
            {
                case "active":
                    QuickPrintDropZone.Background =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(239, 246, 255)
                        );

                    QuickPrintDropZone.BorderBrush =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(37, 99, 235)
                        );
                    break;

                case "success":
                    QuickPrintDropZone.Background =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(240, 253, 244)
                        );

                    QuickPrintDropZone.BorderBrush =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(34, 197, 94)
                        );
                    break;

                case "error":
                    QuickPrintDropZone.Background =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(254, 242, 242)
                        );

                    QuickPrintDropZone.BorderBrush =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(239, 68, 68)
                        );
                    break;

                default:
                    QuickPrintDropZone.Background =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(249, 250, 251)
                        );

                    QuickPrintDropZone.BorderBrush =
                        new Media.SolidColorBrush(
                            Media.Color.FromRgb(203, 213, 225)
                        );
                    break;
            }
        }

        /// <summary>
        /// Carga en el desplegable las impresoras instaladas
        /// en el equipo actual.
        /// </summary>
        private void LoadQuickPrintPrinters()
        {
            if (_quickPrintPrinterService is null)
            {
                QuickPrintPrinterComboBox.ItemsSource = null;
                QuickPrintPrinterComboBox.IsEnabled = false;
                UpdateQuickPrintPrintButtonState();
                return;
            }

            var printers =
                _quickPrintPrinterService.GetInstalledPrinters();

            QuickPrintPrinterComboBox.ItemsSource = printers;
            QuickPrintPrinterComboBox.IsEnabled = printers.Count > 0;

            if (printers.Count > 0)
            {
                var defaultPrinter =
                    printers.FirstOrDefault(printer => printer.IsDefault);

                QuickPrintPrinterComboBox.SelectedItem =
                    defaultPrinter ?? printers[0];
            }

            UpdateQuickPrintPrintButtonState();
        }

        /// <summary>
        /// Activa o desactiva el botón Imprimir
        /// según existan:
        /// - archivos válidos,
        /// - e impresora seleccionada.
        /// </summary>
        private void UpdateQuickPrintPrintButtonState()
        {
            var hasSelectedFiles =
                _selectedQuickPrintFiles.Count > 0;

            var hasSelectedPrinter =
                QuickPrintPrinterComboBox.SelectedItem
                    is QuickPrintPrinterItem;

            QuickPrintPrintButton.IsEnabled =
                hasSelectedFiles &&
                hasSelectedPrinter;
        }

        /// <summary>
        /// Actualiza el estado del botón Imprimir
        /// cuando el usuario cambia de impresora.
        /// </summary>
        private void QuickPrintPrinterComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateQuickPrintPrintButtonState();
        }

        /// <summary>
        /// Carga en el panel de carteles las impresoras disponibles.
        /// Reutiliza el servicio ya creado para impresión rápida.
        /// </summary>
        private void LoadPosterPrinters()
        {
            if (_quickPrintPrinterService is null)
            {
                PosterPrinterComboBox.ItemsSource = null;
                PosterPrinterComboBox.IsEnabled = false;
                return;
            }

            var printers =
                _quickPrintPrinterService.GetInstalledPrinters();

            PosterPrinterComboBox.ItemsSource = printers;
            PosterPrinterComboBox.IsEnabled = printers.Count > 0;

            if (printers.Count > 0)
            {
                var defaultPrinter =
                    printers.FirstOrDefault(printer => printer.IsDefault);

                PosterPrinterComboBox.SelectedItem =
                    defaultPrinter ?? printers[0];
            }
        }

        /// <summary>
        /// Envía a impresión todos los archivos seleccionados
        /// usando la impresora elegida en el desplegable.
        /// </summary>
        private void QuickPrintPrintButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_quickPrintDocumentPrintService is null)
            {
                QuickPrintSelectedFileText.Text =
                    "El servicio de impresión no está inicializado.";

                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            if (_selectedQuickPrintFiles.Count == 0)
            {
                QuickPrintSelectedFileText.Text =
                    "No hay ningún archivo seleccionado.";

                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            if (QuickPrintPrinterComboBox.SelectedItem
                is not QuickPrintPrinterItem selectedPrinter)
            {
                QuickPrintSelectedFileText.Text =
                    "Selecciona una impresora válida.";

                SetQuickPrintDropZoneVisualState("error");
                return;
            }

            QuickPrintPrintButton.IsEnabled = false;

            var totalFiles = _selectedQuickPrintFiles.Count;
            var successCount = 0;
            var failedMessages = new List<string>();

            foreach (var selectedFile in _selectedQuickPrintFiles)
            {
                QuickPrintSelectedFileText.Text =
                    $"Imprimiendo {successCount + failedMessages.Count + 1} de {totalFiles}: {selectedFile.FileName}";

                var result =
                    _quickPrintDocumentPrintService.Print(
                        selectedFile,
                        selectedPrinter
                    );

                if (result.Success)
                {
                    successCount++;
                }
                else
                {
                    failedMessages.Add(
                        $"{selectedFile.FileName}: {result.Message}"
                    );
                }
            }

            if (failedMessages.Count == 0)
            {
                QuickPrintSelectedFileText.Text =
                    totalFiles == 1
                        ? $"Archivo enviado a impresión: {_selectedQuickPrintFiles[0].FileName}"
                        : $"Lote enviado a impresión correctamente. Archivos: {totalFiles}.";

                SetQuickPrintDropZoneVisualState("success");
            }
            else
            {
                QuickPrintSelectedFileText.Text =
                    $"Impresos correctamente: {successCount}/{totalFiles}. Error: {failedMessages[0]}";

                SetQuickPrintDropZoneVisualState("error");
            }

            UpdateQuickPrintPrintButtonState();
        }

        /// <summary>
        /// Obtiene el tipo de búsqueda seleccionado en el panel
        /// y lo convierte al valor que espera la API.
        /// </summary>
        private string GetSelectedPosterSearchType()
        {
            var selectedText =
                (PosterSearchTypeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "EAN";

            return selectedText switch
            {
                "Código artículo" => "ArticleCode",
                "Nombre" => "Name",
                _ => "Ean"
            };
        }

        /// <summary>
        /// Ejecuta la búsqueda de productos contra la API
        /// y muestra los resultados en la lista.
        /// </summary>
        private async void PosterSearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_posterGeneratorService is null)
            {
                PosterStatusText.Text =
                    "El servicio de carteles no está inicializado.";
                return;
            }

            var query =
                PosterSearchTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(query))
            {
                PosterStatusText.Text =
                    "Introduce un texto de búsqueda.";
                return;
            }

            PosterSearchButton.IsEnabled = false;
            PosterStatusText.Text = "Buscando productos...";

            try
            {
                var searchType =
                    GetSelectedPosterSearchType();

                _posterSearchResults =
                    await _posterGeneratorService.SearchProductsAsync(
                        searchType,
                        query,
                        maxResults: 100
                    );

                PosterSearchResultsListBox.ItemsSource = null;
                PosterSearchResultsListBox.ItemsSource = _posterSearchResults;

                PosterStatusText.Text =
                    _posterSearchResults.Count == 0
                        ? "No se han encontrado productos."
                        : $"Productos encontrados: {_posterSearchResults.Count}.";
            }
            catch (Exception ex)
            {
                PosterStatusText.Text =
                    $"Error buscando productos: {ex.Message}";
            }
            finally
            {
                PosterSearchButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Permite buscar productos pulsando Enter
        /// desde el campo de búsqueda.
        /// </summary>
        private void PosterSearchTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;

            PosterSearchButton_Click(
                PosterSearchButton,
                new RoutedEventArgs()
            );

            e.Handled = true;
        }

        /// <summary>
        /// Añade al lote los productos seleccionados en la búsqueda.
        ///
        /// Cada producto se añade con el tamaño y oferta seleccionados
        /// en ese momento en el panel.
        ///
        /// Esto permite que el mismo PDF contenga artículos con
        /// distintos tamaños y distintos tipos de oferta.
        /// </summary>
        private void PosterAddSelectedProductsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItems =
                PosterSearchResultsListBox.SelectedItems
                    .OfType<PosterProductItem>()
                    .ToList();

            if (selectedItems.Count == 0)
            {
                PosterStatusText.Text =
                    "Selecciona al menos un producto de la búsqueda.";

                return;
            }

            var posterSize =
                (PosterSizeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "A4";

            var priceType =
                (PosterPriceTypeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Normal";

            foreach (var product in selectedItems)
            {
                _selectedPosterProducts.Add(new PosterSelectedProductItem
                {
                    Product = product,
                    Size = posterSize,
                    PriceType = priceType
                });
            }

            RefreshSelectedPosterProducts();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                $"Productos añadidos al lote: {selectedItems.Count}.";
        }

        /// <summary>
        /// Quita del lote solo las instancias seleccionadas.
        ///
        /// Como ahora el mismo producto puede aparecer varias veces,
        /// se elimina por InstanceId, no por código de artículo.
        /// </summary>
        private void PosterRemoveSelectedProductsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItems =
                PosterSelectedProductsListBox.SelectedItems
                    .OfType<PosterSelectedProductItem>()
                    .ToList();

            if (selectedItems.Count == 0)
            {
                PosterStatusText.Text =
                    "Selecciona uno o varios productos del lote para quitarlos.";

                return;
            }

            foreach (var item in selectedItems)
            {
                _selectedPosterProducts.RemoveAll(existing =>
                    existing.InstanceId == item.InstanceId
                );
            }

            RefreshSelectedPosterProducts();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                $"Productos eliminados del lote: {selectedItems.Count}.";
        }

        /// <summary>
        /// Refresca visualmente la lista de productos seleccionados
        /// para generar carteles.
        /// </summary>
        private void RefreshSelectedPosterProducts()
        {
            PosterSelectedProductsListBox.ItemsSource = null;
            PosterSelectedProductsListBox.ItemsSource = _selectedPosterProducts;

            PosterSelectedCountText.Text =
                $"Artículos seleccionados: {_selectedPosterProducts.Count}";

            PosterPrintButton.IsEnabled =
                _generatedPosterPdf is not null &&
                PosterPrinterComboBox.SelectedItem is QuickPrintPrinterItem;

            PosterPreviewButton.IsEnabled =
                _generatedPosterPdf is not null;
        }

        /// <summary>
        /// Abre el último PDF de cartel generado usando
        /// el visor PDF predeterminado del equipo.
        ///
        /// No imprime, solo previsualiza.
        /// </summary>
        private void PosterPreviewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_generatedPosterPdf is null ||
                string.IsNullOrWhiteSpace(_generatedPosterPdf.FullPath))
            {
                PosterStatusText.Text =
                    "Primero genera un PDF de cartel válido.";

                PosterPreviewButton.IsEnabled = false;
                return;
            }

            if (!File.Exists(_generatedPosterPdf.FullPath))
            {
                PosterStatusText.Text =
                    "El PDF generado ya no existe o no es accesible.";

                PosterPreviewButton.IsEnabled = false;
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _generatedPosterPdf.FullPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);

                PosterStatusText.Text =
                    $"Previsualizando PDF: {_generatedPosterPdf.FileName}";
            }
            catch (Exception ex)
            {
                PosterStatusText.Text =
                    $"No se ha podido abrir la previsualización: {ex.Message}";
            }
        }

        /// <summary>
        /// Invalida el PDF generado cuando cambia la selección
        /// de productos o las opciones del cartel.
        /// </summary>
        private void InvalidateGeneratedPosterPdf()
        {
            _generatedPosterPdf = null;

            PosterPreviewButton.IsEnabled = false;
            PosterPrintButton.IsEnabled = false;
        }

        /// <summary>
        /// Vacía completamente el lote de productos seleccionados
        /// para generar carteles.
        /// </summary>
        private void PosterClearSelectedProductsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedPosterProducts.Count == 0)
            {
                PosterStatusText.Text =
                    "No hay artículos seleccionados para quitar.";

                return;
            }

            var removedCount =
                _selectedPosterProducts.Count;

            _selectedPosterProducts.Clear();

            RefreshSelectedPosterProducts();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                $"Se han quitado todos los artículos del lote. Total: {removedCount}.";
        }

        /// <summary>
        /// Actualiza el texto de ayuda según el tipo de búsqueda elegido.
        /// </summary>
        private void UpdatePosterSearchHelpText()
        {
            var searchType =
                GetSelectedPosterSearchType();

            PosterSearchHelpText.Text = searchType switch
            {
                "ArticleCode" =>
                    "Introduce el código interno del artículo. Ejemplo: 02016033.",

                "Name" =>
                    "Introduce parte del nombre del producto. Ejemplo: patata, mesa, monster.",

                _ =>
                    "El código EAN debe tener 5, 8, 13 o 14 dígitos exactos."
            };
        }

        private void PosterSearchTypeComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PosterSearchHelpText is null)
                return;

            UpdatePosterSearchHelpText();
        }

        /// <summary>
        /// Refresca visualmente la lista de etiquetas seleccionadas.
        /// </summary>
        private void RefreshSelectedLabels()
        {
            LabelSelectedListBox.ItemsSource = null;
            LabelSelectedListBox.ItemsSource = _selectedLabels;

            LabelSelectedCountText.Text =
                $"Etiquetas seleccionadas: {_selectedLabels.Count}";

            PosterPrintButton.IsEnabled =
                _generatedPosterPdf is not null &&
                PosterPrinterComboBox.SelectedItem is QuickPrintPrinterItem;

            PosterPreviewButton.IsEnabled =
                _generatedPosterPdf is not null;
        }

        /// <summary>
        /// Carga desde la API las etiquetas pendientes del año actual.
        /// </summary>
        private async Task LoadPendingLabelsAsync()
        {
            if (_posterGeneratorService is null)
            {
                PosterStatusText.Text =
                    "El servicio de etiquetas no está inicializado.";
                return;
            }

            PosterStatusText.Text =
                "Cargando etiquetas pendientes...";

            _pendingLabels =
                await _posterGeneratorService.GetPendingLabelsAsync(
                    warehouse: "01",
                    maxResults: 1000
                );

            LabelPendingListBox.ItemsSource = null;
            LabelPendingListBox.ItemsSource = _pendingLabels;

            PosterStatusText.Text =
                _pendingLabels.Count == 0
                    ? "No hay etiquetas pendientes de impresión."
                    : $"Etiquetas pendientes encontradas: {_pendingLabels.Count}.";
        }

        /// <summary>
        /// Indica si el panel está trabajando en modo Etiqueta.
        /// </summary>
        private bool IsLabelModeSelected()
        {
            var selectedText =
                (PosterTypeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Cartel";

            return string.Equals(
                selectedText,
                "Etiqueta",
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>
        /// Cambia el contenido visible entre Carteles y Etiquetas.
        /// </summary>
        private async void PosterTypeComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PosterModeContentGrid is null ||
                LabelModeContentGrid is null)
            {
                return;
            }

            var isLabelMode =
                IsLabelModeSelected();

            PosterModeContentGrid.Visibility =
                isLabelMode
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            LabelModeContentGrid.Visibility =
                isLabelMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            InvalidateGeneratedPosterPdf();

            if (isLabelMode && _pendingLabels.Count == 0)
            {
                await LoadPendingLabelsAsync();
            }

            PosterStatusText.Text =
                isLabelMode
                    ? "Modo etiquetas activo. Selecciona etiquetas pendientes para generar el PDF."
                    : "Modo carteles activo. Busca productos y añádelos al lote.";
        }

        /// <summary>
        /// Recarga manualmente las etiquetas pendientes.
        /// </summary>
        private async void LabelRefreshPendingButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LabelRefreshPendingButton.IsEnabled = false;

            try
            {
                await LoadPendingLabelsAsync();
            }
            finally
            {
                LabelRefreshPendingButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Añade al lote las etiquetas pendientes seleccionadas.
        /// No permite duplicar la misma etiqueta.
        /// </summary>
        private void LabelAddSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItems =
                LabelPendingListBox.SelectedItems
                    .OfType<PendingLabelItem>()
                    .ToList();

            if (selectedItems.Count == 0)
            {
                PosterStatusText.Text =
                    "Selecciona al menos una etiqueta pendiente.";

                return;
            }

            var addedCount = 0;

            foreach (var label in selectedItems)
            {
                var alreadyExists =
                    _selectedLabels.Any(existing =>
                        existing.ReturnRowId == label.ReturnRowId
                    );

                if (alreadyExists)
                    continue;

                _selectedLabels.Add(label);
                addedCount++;
            }

            RefreshSelectedLabels();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                addedCount == 0
                    ? "Las etiquetas seleccionadas ya estaban añadidas."
                    : $"Etiquetas añadidas al lote: {addedCount}.";
        }

        /// <summary>
        /// Quita del lote las etiquetas seleccionadas.
        /// </summary>
        private void LabelRemoveSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItems =
                LabelSelectedListBox.SelectedItems
                    .OfType<PendingLabelItem>()
                    .ToList();

            if (selectedItems.Count == 0)
            {
                PosterStatusText.Text =
                    "Selecciona una o varias etiquetas del lote para quitarlas.";

                return;
            }

            foreach (var label in selectedItems)
            {
                _selectedLabels.RemoveAll(existing =>
                    existing.ReturnRowId == label.ReturnRowId
                );
            }

            RefreshSelectedLabels();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                $"Etiquetas eliminadas del lote: {selectedItems.Count}.";
        }

        /// <summary>
        /// Vacía el lote completo de etiquetas seleccionadas.
        /// </summary>
        private void LabelClearSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedLabels.Count == 0)
            {
                PosterStatusText.Text =
                    "No hay etiquetas seleccionadas para quitar.";

                return;
            }

            var removedCount =
                _selectedLabels.Count;

            _selectedLabels.Clear();

            RefreshSelectedLabels();
            InvalidateGeneratedPosterPdf();

            PosterStatusText.Text =
                $"Se han quitado todas las etiquetas del lote. Total: {removedCount}.";
        }

        /// <summary>
        /// Genera el PDF de etiquetas desde el lote seleccionado.
        /// </summary>
        private async Task GenerateLabelsFromDesktopAsync()
        {
            if (_selectedLabels.Count == 0)
            {
                PosterStatusText.Text =
                    "Añade al menos una etiqueta al lote antes de generar el PDF.";

                PosterPrintButton.IsEnabled = false;
                PosterPreviewButton.IsEnabled = false;
                return;
            }

            PosterGenerateButton.IsEnabled = false;
            PosterPrintButton.IsEnabled = false;
            PosterPreviewButton.IsEnabled = false;

            _generatedPosterPdf = null;

            var labelFormat =
                GetSelectedLabelFormat() ?? "Normal";

            PosterStatusText.Text =
                $"Generando PDF de etiquetas. Formato: {labelFormat}. Etiquetas: {_selectedLabels.Count}...";

            var result =
                await _posterGeneratorService!.GenerateLabelsPdfAsync(
                    _selectedLabels,
                    labelFormat
                );

            PosterStatusText.Text =
                result.Message;

            if (!result.Success || result.GeneratedPdf is null)
            {
                PosterPrintButton.IsEnabled = false;
                PosterPreviewButton.IsEnabled = false;
                PosterGenerateButton.IsEnabled = true;
                return;
            }

            _generatedPosterPdf =
                result.GeneratedPdf;

            PosterPreviewButton.IsEnabled = true;

            PosterPrintButton.IsEnabled =
                PosterPrinterComboBox.SelectedItem is QuickPrintPrinterItem;

            PosterGenerateButton.IsEnabled = true;
        }


        //METODOS DE REFERENCIA EN LA PROGRAMACION DE EVENTOS / TAREAS
        /// <summary>
        /// Botón flotante de tareas.
        /// Abre/cierra el panel de tareas y cierra el resto de paneles flotantes.
        /// </summary>
        private async void FloatingTasksButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (TasksPanel.Visibility == Visibility.Visible)
            {
                HideTasksPanel();
                return;
            }

            if (ChatPanel.Visibility == Visibility.Visible)
            {
                HideChatAnimated(showMinimizeTip: false);
            }

            if (QuickPrintPanel.Visibility == Visibility.Visible)
            {
                HideQuickPrintPanelAnimated();
            }

            if (PosterPanel.Visibility == Visibility.Visible)
            {
                HidePosterPanelAnimated();
            }

            TasksPanel.Visibility = Visibility.Visible;
            TasksPanel.Opacity = 1;

            TaskCreatedAtTextBox.Text =
                DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            if (TaskDueDatePicker.SelectedDate is null)
            {
                TaskDueDatePicker.SelectedDate =
                    DateTime.Today;
            }

            await LoadDesktopTaskGroupsAsync();
            await LoadDesktopTasksAsync();
        }

        /// <summary>
        /// Oculta el panel de tareas y su detalle.
        /// </summary>
        private void HideTasksPanel()
        {
            if (TasksPanel is not null)
            {
                TasksPanel.Visibility = Visibility.Collapsed;
                TasksPanel.Opacity = 0;
            }

            if (TaskDetailPanel is not null)
            {
                TaskDetailPanel.Visibility = Visibility.Collapsed;
                TaskDetailPanel.Opacity = 0;
            }
        }

        private async Task LoadDesktopTasksAsync()
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            TasksStatusText.Text =
                "Cargando tareas...";

            _desktopTasks =
                (await _desktopTaskService.GetTasksAsync())
                    .OrderBy(task => GetDesktopTaskPriorityOrder(task.Priority))
                    .ThenBy(task => task.DueAt)
                    .ThenBy(task => task.CreatedAt)
                    .ToList();

            TasksListBox.ItemsSource = null;
            TasksListBox.ItemsSource = _desktopTasks;

            TasksStatusText.Text =
                _desktopTasks.Count == 0
                    ? "No hay tareas creadas."
                    : $"Tareas cargadas: {_desktopTasks.Count}.";
        }

        private static int GetDesktopTaskPriorityOrder(
            string? priority)
        {
            var normalized =
                (priority ?? "")
                    .Trim()
                    .ToUpperInvariant()
                    .Replace(" ", "")
                    .Replace("-", "")
                    .Replace("_", "");

            return normalized switch
            {
                "MUYURGENTE" => 0,
                "URGENTE" => 1,
                _ => 2
            };
        }

        /// <summary>
        /// Aplica los filtros visuales de grupo, urgencia y estado
        /// sobre las tareas ya permitidas por la API.
        /// </summary>
        private void ApplyDesktopTaskFilters()
        {
            IEnumerable<DesktopTaskItem> query =
                _desktopTasks;

            var selectedGroup =
                TaskFilterGroupComboBox.SelectedItem as DesktopTaskGroupItem;

            if (selectedGroup is not null &&
                !string.IsNullOrWhiteSpace(selectedGroup.Name))
            {
                query =
                    query.Where(task =>
                        string.Equals(
                            task.GroupName,
                            selectedGroup.Name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            var selectedPriority =
                (TaskFilterPriorityComboBox.SelectedItem as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Todas";

            query = selectedPriority switch
            {
                "Muy urgente" =>
                    query.Where(task =>
                        string.Equals(
                            task.Priority,
                            "MuyUrgente",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),

                "Urgente" =>
                    query.Where(task =>
                        string.Equals(
                            task.Priority,
                            "Urgente",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),

                "Normal" =>
                    query.Where(task =>
                        string.Equals(
                            task.Priority,
                            "Normal",
                            StringComparison.OrdinalIgnoreCase
                        )
                    ),

                _ => query
            };

            var selectedStatus =
                (TaskFilterStatusComboBox.SelectedItem as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Todos";

            query = selectedStatus switch
            {
                "En plazo" =>
                    query.Where(task => !task.IsExpired),

                "Fuera de plazo" =>
                    query.Where(task => task.IsExpired),

                _ => query
            };

            var assignedToFilter =
                TaskFilterAssignedToTextBox.Text?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(assignedToFilter))
            {
                query =
                    query.Where(task =>
                        !string.IsNullOrWhiteSpace(task.AssignedTo) &&
                        task.AssignedTo.Contains(
                            assignedToFilter,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            _filteredDesktopTasks =
                query
                    .OrderBy(task => GetDesktopTaskPriorityOrder(task.Priority))
                    .ThenBy(task => task.DueAt)
                    .ThenBy(task => task.CreatedAt)
                    .ToList();

            TasksListBox.ItemsSource = null;
            TasksListBox.ItemsSource = _filteredDesktopTasks;

            TasksStatusText.Text =
                _desktopTasks.Count == 0
                    ? "No hay tareas creadas."
                    : $"Tareas visibles: {_filteredDesktopTasks.Count} de {_desktopTasks.Count}.";
        }

        /// <summary>
        /// Se ejecuta cuando cambia cualquier filtro visual de tareas.
        /// </summary>
        private void TaskFilters_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (TasksListBox is null ||
                TaskFilterGroupComboBox is null ||
                TaskFilterPriorityComboBox is null ||
                TaskFilterStatusComboBox is null)
            {
                return;
            }

            ApplyDesktopTaskFilters();
        }

        private void TaskFilterAssignedToTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (TasksListBox is null)
                return;

            ApplyDesktopTaskFilters();
        }

        private async void TasksRefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadDesktopTasksAsync();
        }

        private async void TaskAddButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            var writtenBy =
                TaskWrittenByTextBox.Text?.Trim() ?? "";

            var assignedTo =
                TaskAssignedToTextBox.Text?.Trim() ?? "";

            var description =
                TaskDescriptionTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(writtenBy))
            {
                TasksStatusText.Text =
                    "Indica quién escribe la tarea.";

                return;
            }

            if (string.IsNullOrWhiteSpace(assignedTo))
            {
                TasksStatusText.Text =
                    "Indica para quién va dirigida.";

                return;
            }

            if (TaskDueDatePicker.SelectedDate is null)
            {
                TasksStatusText.Text =
                    "Selecciona fecha de vencimiento.";

                return;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                TasksStatusText.Text =
                    "Escribe la descripción de la tarea.";

                return;
            }

            if (TaskGroupComboBox.SelectedItem is not DesktopTaskGroupItem selectedGroup)
            {
                TasksStatusText.Text =
                    "Selecciona el grupo al que enviar la tarea.";

                return;
            }

            var selectedPriority =
                (TaskPriorityComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "Normal";

            var priority =
                selectedPriority switch
                {
                    "Muy urgente" => "MuyUrgente",
                    "Urgente" => "Urgente",
                    _ => "Normal"
                };

            TaskAddButton.IsEnabled = false;

            try
            {
                var request =
                    new CreateDesktopTaskRequest
                    {
                        WrittenBy = writtenBy,
                        AssignedTo = assignedTo,
                        DueAt = TaskDueDatePicker.SelectedDate.Value.Date.AddHours(23).AddMinutes(59),
                        Description = description,
                        GroupName = selectedGroup.Name,
                        Priority = priority
                    };

                var createdTask =
                    await _desktopTaskService.CreateTaskAsync(
                        request
                    );

                if (createdTask is null)
                {
                    TasksStatusText.Text =
                        "No se ha podido crear la tarea.";

                    return;
                }

                var uploadedCount = 0;
                var failedUploads = new List<string>();

                foreach (var attachmentPath in _newTaskAttachmentPaths)
                {
                    var uploadResult =
                        await _desktopTaskService.UploadAttachmentAsync(
                            createdTask,
                            attachmentPath
                        );

                    if (uploadResult.Success)
                    {
                        uploadedCount++;
                    }
                    else
                    {
                        failedUploads.Add(
                            $"{Path.GetFileName(attachmentPath)}: {uploadResult.Message}"
                        );
                    }
                }

                TaskDescriptionTextBox.Clear();
                _newTaskAttachmentPaths.Clear();
                RefreshNewTaskAttachmentsText();

                if (failedUploads.Count == 0)
                {
                    TasksStatusText.Text =
                        uploadedCount == 0
                            ? "Tarea creada correctamente."
                            : $"Tarea creada correctamente con {uploadedCount} archivo/s adjunto/s.";
                }
                else
                {
                    TasksStatusText.Text =
                        $"Tarea creada, pero algunos adjuntos fallaron: {failedUploads[0]}";
                }

                await LoadDesktopTasksAsync();
            }
            finally
            {
                TaskAddButton.IsEnabled = true;
            }
        }

        private DesktopTaskItem? GetSingleSelectedTask()
        {
            return TasksListBox.SelectedItem as DesktopTaskItem;
        }

        private List<DesktopTaskItem> GetSelectedTasks()
        {
            return TasksListBox.SelectedItems
                .OfType<DesktopTaskItem>()
                .ToList();
        }

        private void TaskShowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var task =
                GetSingleSelectedTask();

            if (task is null)
            {
                TasksStatusText.Text =
                    "Selecciona una tarea para mostrar el detalle.";

                return;
            }

            ShowTaskDetail(task);
        }

        private void TasksListBox_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            var task =
                GetSingleSelectedTask();

            if (task is null)
                return;

            ShowTaskDetail(task);
        }

        private void ShowTaskDetail(
            DesktopTaskItem task)
        {
            _currentTaskDetail =
                task;

            TaskDetailWrittenByText.Text =
                task.WrittenBy;

            TaskDetailAssignedToText.Text =
                task.AssignedTo;

            TaskDetailCreatedAtText.Text =
                task.CreatedAt.ToString("dd/MM/yyyy HH:mm");

            TaskDetailDueAtText.Text =
                task.DueAt.ToString("dd/MM/yyyy HH:mm");

            TaskDetailGroupText.Text =
                string.IsNullOrWhiteSpace(task.GroupDisplayName)
                    ? task.GroupName
                    : task.GroupDisplayName;

            TaskDetailPriorityText.Text =
                task.PriorityDisplayText;

            TaskDetailDescriptionText.Text =
                task.Description;

            TaskDetailDescriptionEditTextBox.Text =
                task.Description;

            SetTaskDescriptionEditMode(false);

            TaskDetailNewDueDatePicker.SelectedDate =
                task.DueAt.Date.AddDays(1);

            TaskAttachmentsListBox.ItemsSource = null;
            TaskAttachmentsListBox.ItemsSource = task.Attachments;

            TaskDetailPanel.Visibility = Visibility.Visible;
            TaskDetailPanel.Opacity = 1;
        }

        private void TaskDetailCloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            TaskDetailPanel.Visibility = Visibility.Collapsed;
            TaskDetailPanel.Opacity = 0;

            _currentTaskDetail = null;

            SetTaskDescriptionEditMode(false);
        }

        private async void TaskDeleteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            var selectedTasks =
                GetSelectedTasks();

            if (selectedTasks.Count == 0)
            {
                TasksStatusText.Text =
                    "Selecciona una o varias tareas para eliminar.";

                return;
            }

            TaskDeleteButton.IsEnabled = false;

            try
            {
                var result =
                    await _desktopTaskService.DeleteTasksAsync(
                        selectedTasks
                    );

                TasksStatusText.Text =
                    result.Message;

                await LoadDesktopTasksAsync();

                TaskDetailPanel.Visibility = Visibility.Collapsed;
                TaskDetailPanel.Opacity = 0;
            }
            finally
            {
                TaskDeleteButton.IsEnabled = true;
            }
        }

        private async Task LoadDesktopTaskGroupsAsync()
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            _desktopTaskGroups =
                await _desktopTaskService.GetGroupsAsync();

            TaskGroupComboBox.ItemsSource = null;
            TaskGroupComboBox.ItemsSource = _desktopTaskGroups;
            TaskGroupComboBox.DisplayMemberPath = "DisplayText";

            if (_desktopTaskGroups.Count > 0)
            {
                TaskGroupComboBox.SelectedIndex = 0;
            }

            var filterGroups =
                new List<DesktopTaskGroupItem>
                {
                    new DesktopTaskGroupItem
                    {
                        Name = "",
                        DisplayName = "Todos los grupos"
                    }
                };

            filterGroups.AddRange(_desktopTaskGroups);

            TaskFilterGroupComboBox.ItemsSource = null;
            TaskFilterGroupComboBox.ItemsSource = filterGroups;
            TaskFilterGroupComboBox.DisplayMemberPath = "DisplayText";
            TaskFilterGroupComboBox.SelectedIndex = 0;

            TaskFilterPriorityComboBox.SelectedIndex = 0;
            TaskFilterStatusComboBox.SelectedIndex = 0;

            TaskAddButton.IsEnabled =
                _desktopTaskGroups.Count > 0;

            if (_desktopTaskGroups.Count == 0)
            {
                TasksStatusText.Text =
                    "Este usuario/equipo no pertenece a ningún grupo de tareas.";
            }
        }

        private async void TaskPostponeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            if (TaskDetailNewDueDatePicker.SelectedDate is null)
            {
                TasksStatusText.Text =
                    "Selecciona una nueva fecha límite.";

                return;
            }

            var selectedDate =
                TaskDetailNewDueDatePicker.SelectedDate.Value.Date;

            if (selectedDate <= DateTime.Today)
            {
                TasksStatusText.Text =
                    "La nueva fecha límite debe ser posterior a hoy.";

                return;
            }

            if (selectedDate <= _currentTaskDetail.DueAt.Date)
            {
                TasksStatusText.Text =
                    "La nueva fecha debe ser posterior a la fecha límite actual.";

                return;
            }

            var newDueAt =
                selectedDate
                    .AddHours(23)
                    .AddMinutes(59);

            TaskPostponeButton.IsEnabled = false;

            try
            {
                var result =
                    await _desktopTaskService.PostponeTaskAsync(
                        _currentTaskDetail,
                        newDueAt
                    );

                TasksStatusText.Text =
                    result.Message;

                if (!result.Success)
                    return;

                await LoadDesktopTasksAsync();

                if (result.Task is not null)
                {
                    ShowTaskDetail(result.Task);
                }
                else
                {
                    TaskDetailPanel.Visibility = Visibility.Collapsed;
                    TaskDetailPanel.Opacity = 0;
                    _currentTaskDetail = null;
                }
            }
            finally
            {
                TaskPostponeButton.IsEnabled = true;
            }
        }

        private void SetTaskDescriptionEditMode(
            bool isEditing)
        {
            _isEditingTaskDescription =
                isEditing;

            TaskDetailDescriptionText.Visibility =
                isEditing
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            TaskDetailDescriptionEditTextBox.Visibility =
                isEditing
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            TaskEditDescriptionButton.Visibility =
                isEditing
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            TaskSaveDescriptionButton.Visibility =
                isEditing
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            TaskCancelDescriptionEditButton.Visibility =
                isEditing
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (isEditing)
            {
                TaskDetailDescriptionEditTextBox.Focus();
                TaskDetailDescriptionEditTextBox.SelectAll();
            }
        }

        private void TaskEditDescriptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            TaskDetailDescriptionEditTextBox.Text =
                _currentTaskDetail.Description;

            SetTaskDescriptionEditMode(true);
        }

        private void TaskCancelDescriptionEditButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_currentTaskDetail is not null)
            {
                TaskDetailDescriptionEditTextBox.Text =
                    _currentTaskDetail.Description;
            }

            SetTaskDescriptionEditMode(false);
        }

        private async void TaskSaveDescriptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            var newDescription =
                TaskDetailDescriptionEditTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(newDescription))
            {
                TasksStatusText.Text =
                    "La descripción no puede estar vacía.";

                return;
            }

            if (string.Equals(
                newDescription,
                _currentTaskDetail.Description,
                StringComparison.Ordinal))
            {
                TasksStatusText.Text =
                    "No se han detectado cambios en la descripción.";

                SetTaskDescriptionEditMode(false);
                return;
            }

            TaskSaveDescriptionButton.IsEnabled = false;
            TaskCancelDescriptionEditButton.IsEnabled = false;

            try
            {
                var result =
                    await _desktopTaskService.UpdateTaskDescriptionAsync(
                        _currentTaskDetail,
                        newDescription
                    );

                TasksStatusText.Text =
                    result.Message;

                if (!result.Success)
                    return;

                await LoadDesktopTasksAsync();

                if (result.Task is not null)
                {
                    ShowTaskDetail(result.Task);
                }
                else
                {
                    _currentTaskDetail.Description =
                        newDescription;

                    TaskDetailDescriptionText.Text =
                        newDescription;

                    SetTaskDescriptionEditMode(false);
                }
            }
            finally
            {
                TaskSaveDescriptionButton.IsEnabled = true;
                TaskCancelDescriptionEditButton.IsEnabled = true;
            }
        }

        private async void TaskUploadAttachmentButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            var dialog =
                new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Seleccionar archivo para adjuntar",
                    Filter = "Todos los archivos (*.*)|*.*",
                    Multiselect = false
                };

            if (dialog.ShowDialog() != true)
                return;

            TaskUploadAttachmentButton.IsEnabled = false;

            try
            {
                TasksStatusText.Text =
                    "Subiendo archivo adjunto...";

                var result =
                    await _desktopTaskService.UploadAttachmentAsync(
                        _currentTaskDetail,
                        dialog.FileName
                    );

                TasksStatusText.Text =
                    result.Message;

                if (!result.Success)
                    return;

                await LoadDesktopTasksAsync();

                if (result.Task is not null)
                {
                    ShowTaskDetail(result.Task);
                }
            }
            finally
            {
                TaskUploadAttachmentButton.IsEnabled = true;
            }
        }

        private async void TaskDownloadAttachmentButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            if (TaskAttachmentsListBox.SelectedItem
                is not DesktopTaskAttachmentItem selectedAttachment)
            {
                TasksStatusText.Text =
                    "Selecciona un archivo adjunto para descargar.";

                return;
            }

            TaskDownloadAttachmentButton.IsEnabled = false;

            try
            {
                TasksStatusText.Text =
                    "Descargando archivo adjunto...";

                var result =
                    await _desktopTaskService.DownloadAttachmentAsync(
                        _currentTaskDetail,
                        selectedAttachment
                    );

                if (!result.Success)
                {
                    TasksStatusText.Text =
                        result.Message;

                    return;
                }

                var dialog =
                    new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Guardar archivo adjunto",
                        FileName = result.FileName,
                        Filter = "Todos los archivos (*.*)|*.*"
                    };

                if (dialog.ShowDialog() != true)
                {
                    TasksStatusText.Text =
                        "Descarga cancelada.";

                    return;
                }

                await File.WriteAllBytesAsync(
                    dialog.FileName,
                    result.FileBytes
                );

                TasksStatusText.Text =
                    $"Archivo descargado: {Path.GetFileName(dialog.FileName)}";
            }
            finally
            {
                TaskDownloadAttachmentButton.IsEnabled = true;
            }
        }

        private void TaskSelectNewAttachmentsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Seleccionar archivos para adjuntar",
                    Filter = "Todos los archivos (*.*)|*.*",
                    Multiselect = true
                };

            if (dialog.ShowDialog() != true)
                return;

            foreach (var fileName in dialog.FileNames)
            {
                if (!_newTaskAttachmentPaths.Contains(
                        fileName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    _newTaskAttachmentPaths.Add(fileName);
                }
            }

            RefreshNewTaskAttachmentsText();
        }

        private void TaskClearNewAttachmentsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _newTaskAttachmentPaths.Clear();

            RefreshNewTaskAttachmentsText();
        }

        private void RefreshNewTaskAttachmentsText()
        {
            if (_newTaskAttachmentPaths.Count == 0)
            {
                TaskNewAttachmentsText.Text =
                    "Sin archivos adjuntos.";

                return;
            }

            TaskNewAttachmentsText.Text =
                _newTaskAttachmentPaths.Count == 1
                    ? $"1 archivo: {Path.GetFileName(_newTaskAttachmentPaths[0])}"
                    : $"{_newTaskAttachmentPaths.Count} archivos seleccionados.";
        }

        private async void TaskDeleteAttachmentButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_desktopTaskService is null)
            {
                TasksStatusText.Text =
                    "El servicio de tareas no está inicializado.";

                return;
            }

            if (_currentTaskDetail is null)
            {
                TasksStatusText.Text =
                    "No hay ninguna tarea abierta en detalle.";

                return;
            }

            if (TaskAttachmentsListBox.SelectedItem
                is not DesktopTaskAttachmentItem selectedAttachment)
            {
                TasksStatusText.Text =
                    "Selecciona un archivo adjunto para eliminar.";

                return;
            }

            var confirm =
                System.Windows.MessageBox.Show(
                    this,
                    $"¿Quieres eliminar el archivo adjunto?\n\n{selectedAttachment.FileName}",
                    "Eliminar adjunto",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

            if (confirm != MessageBoxResult.Yes)
                return;

            TaskDeleteAttachmentButton.IsEnabled = false;

            try
            {
                TasksStatusText.Text =
                    "Eliminando archivo adjunto...";

                var result =
                    await _desktopTaskService.DeleteAttachmentAsync(
                        _currentTaskDetail,
                        selectedAttachment
                    );

                TasksStatusText.Text =
                    result.Message;

                if (!result.Success)
                    return;

                await LoadDesktopTasksAsync();

                if (result.Task is not null)
                {
                    ShowTaskDetail(result.Task);
                }
                else
                {
                    TaskDetailPanel.Visibility = Visibility.Collapsed;
                    TaskDetailPanel.Opacity = 0;
                    _currentTaskDetail = null;
                }
            }
            finally
            {
                TaskDeleteAttachmentButton.IsEnabled = true;
            }
        }
    }
}
