using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Collections.Concurrent;
using System.Globalization;
using TramitesComunicacionCRC.Services;

namespace TramitesComunicacionCRC;

public partial class MainPage : ContentPage
{
    private string rutaExcelGlobal;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnLoadCsvClicked(object sender, EventArgs e)
    {
        var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
            { DevicePlatform.Android, new[] { "text/csv" } },
            { DevicePlatform.WinUI, new[] { ".csv" } },
            { DevicePlatform.MacCatalyst, new[] { "csv" } }
        });

        var options = new PickOptions
        {
            PickerTitle = "Seleccione un archivo CSV",
            FileTypes = customFileType,
        };

        var result = await FilePicker.PickAsync(options);
        if (result != null)
        {
            await ConsultarPorTelefono(result.FullPath);
        }
        else
        {
            await DisplayAlert("Error", "No se seleccionó ningún archivo", "OK");
        }
    }

    public async Task ConsultarPorTelefono(string rutaArchivo)
    {
        try
        {
            Loader.IsVisible = true; 
            Loader.IsRunning = true; 

            var telefonos = await LeerTelefonosDesdeCsvAsync(rutaArchivo);
            if (telefonos == null || telefonos.Length == 0)
            {
                await DisplayAlert("Error", "No se encontraron teléfonos en el archivo.", "OK");
                return;
            }

            var webServiceClient = new WebServiceClient();
            string resultByPhone = await webServiceClient.ConsultarRnePorTelefonoAsync(telefonos);

            string documentosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            rutaExcelGlobal = Path.Combine(documentosPath, "ResultadosConsulta.xlsx");
            ExportarJsonAExcel(resultByPhone, rutaExcelGlobal);

            DownloadExcelBtn.IsVisible = true;
            MostrarDatosEnTabla(rutaExcelGlobal);

            await DisplayAlert("Éxito", "Consulta realizada y resultados exportados", "OK");
        }
        catch (FileNotFoundException ex)
        {
            await DisplayAlert("Error", "Archivo no encontrado: " + ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", "Error al procesar el archivo: " + ex.Message, "OK");
        }
        finally
        {
            Loader.IsVisible = false; 
            Loader.IsRunning = false;
        }
    }


    public static async Task<string[]> LeerTelefonosDesdeCsvAsync(string rutaArchivo)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = false
        };

        var telefonos = new ConcurrentBag<string>();

        try
        {
            using (var reader = new StreamReader(rutaArchivo))
            using (var csv = new CsvReader(reader, config))
            {
                while (await csv.ReadAsync())
                {
                    var telefono = csv.GetField<string>(0);
                    if (telefono != null)
                    {
                        telefonos.Add(telefono);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al leer el archivo CSV: {ex.Message}");
            throw;
        }

        return telefonos.ToArray();
    }
    public static void ExportarJsonAExcel(string jsonData, string rutaArchivo)
    {
        var datos = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(jsonData);
        OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

        FileInfo file = new FileInfo(rutaArchivo);
        if (file.Exists)
        {
            file.Delete();
        }

        using (var package = new ExcelPackage(file))
        {
            var worksheet = package.Workbook.Worksheets.Add("Datos");

            int columnIndex = 1;
            foreach (var key in datos[0].Keys)
            {
                if (key == "opcionesContacto")
                {
                    worksheet.Cells[1, columnIndex].Value = "Sms";
                    columnIndex++;
                    worksheet.Cells[1, columnIndex].Value = "Aplicacion";
                    columnIndex++;
                    worksheet.Cells[1, columnIndex].Value = "Llamada";
                }
                else
                {
                    string header = key == "llave" ? "Telefono" : Capitalize(key);
                    worksheet.Cells[1, columnIndex].Value = header;
                }
                columnIndex++;
            }

            worksheet.Cells[1, columnIndex].Value = "Fecha Consultada";
            string fechaConsultada = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            int rowIndex = 2;
            foreach (var item in datos)
            {
                columnIndex = 1;
                foreach (var key in item.Keys)
                {
                    if (key == "opcionesContacto")
                    {
                        var opciones = JsonConvert.DeserializeObject<Dictionary<string, bool>>(item[key].ToString());
                        worksheet.Cells[rowIndex, columnIndex].Value = opciones["sms"] ? "V" : "F";
                        columnIndex++;
                        worksheet.Cells[rowIndex, columnIndex].Value = opciones["aplicacion"] ? "V" : "F";
                        columnIndex++;
                        worksheet.Cells[rowIndex, columnIndex].Value = opciones["llamada"] ? "V" : "F";
                    }
                    else
                    {
                        worksheet.Cells[rowIndex, columnIndex].Value = item[key]?.ToString();
                    }
                    columnIndex++;
                }
                worksheet.Cells[rowIndex, columnIndex].Value = fechaConsultada;
                rowIndex++;
            }

            package.Save();
        }
    }
    public static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = text.ToLower();
        string[] words = text.Split(new char[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = words[i].Substring(0, 1).ToUpper() + words[i].Substring(1);
        }
        return string.Join(" ", words);
    }
    private async void OnDownloadExcelClicked(object sender, EventArgs e)
    {
        try
        {
            await DisplayAlert("Descargar Archivo", "El archivo se descargará desde: " + rutaExcelGlobal, "OK");

            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = rutaExcelGlobal,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", "Error al abrir el archivo: " + ex.Message, "OK");
        }
    }
    private void OnClearBtnClicked(object sender, EventArgs e)
    {
        DownloadExcelBtn.IsVisible = false;
        DataCollectionView.ItemsSource = null;
    }
    private void MostrarDatosEnTabla(string rutaArchivo)
    {
        var datos = LeerExcel(rutaArchivo);
        Console.WriteLine($"Número de filas leídas: {datos.Count}");

        var transformedData = datos.Select(d => new ExcelRow
        {
            Telefono = d.ContainsKey("Telefono") ? d["Telefono"] : string.Empty,
            Sms = d.ContainsKey("Sms") ? d["Sms"] : string.Empty,
            Aplicacion = d.ContainsKey("Aplicacion") ? d["Aplicacion"] : string.Empty,
            Llamada = d.ContainsKey("Llamada") ? d["Llamada"] : string.Empty,
            Tipo = d.ContainsKey("Tipo") ? d["Tipo"] : string.Empty,
            Fechacrea = d.ContainsKey("Fechacreacion") ? d["Fechacreacion"] : string.Empty,
            FechaConsultada = d.ContainsKey("Fecha Consultada") ? d["Fecha Consultada"] : string.Empty
        }).ToList();

        DataCollectionView.ItemsSource = transformedData;
    }
    private List<Dictionary<string, string>> LeerExcel(string rutaArchivo)
    {
        var datos = new List<Dictionary<string, string>>();
        using (var package = new ExcelPackage(new FileInfo(rutaArchivo)))
        {
            var worksheet = package.Workbook.Worksheets[0];
            var columnCount = worksheet.Dimension.End.Column;
            var rowCount = worksheet.Dimension.End.Row;

            var headers = new List<string>();
            for (int col = 1; col <= columnCount; col++)
            {
                headers.Add(worksheet.Cells[1, col].Text);
            }

            for (int row = 2; row <= rowCount; row++)
            {
                var rowData = new Dictionary<string, string>();
                for (int col = 1; col <= columnCount; col++)
                {
                    rowData[headers[col - 1]] = worksheet.Cells[row, col].Text;
                }
                datos.Add(rowData);
            }
        }
        return datos;
    }
    private class ExcelRow
    {
        public string Telefono { get; set; }
        public string Sms { get; set; }
        public string Aplicacion { get; set; }
        public string Llamada { get; set; }
        public string Tipo { get; set; }
        public string Fechacrea { get; set; }
        public string FechaConsultada { get; set; }
    }
}
