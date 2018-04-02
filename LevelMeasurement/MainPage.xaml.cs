using MetroLog;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace LevelMeasurement
{
       public struct AtCommandScript
    {
        public string atCommand;
        public string successString;
        public string failString;
        public int preCommandDelay;
        public int postCommandDelay;
     }

    public enum aTCommandResponse
        { ok, error, unknown}

    public struct modemResponse
    {
        public aTCommandResponse responseStatus;
        public string responseString;
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// <summary>
    /// Implementation of <see cref="INotifyPropertyChanged"/> to simplify models.
    /// </summary>
    [Windows.Foundation.Metadata.WebHostHidden]
    public class DeviceViewModel : INotifyPropertyChanged
    {
        private DeviceInformation di;
        private string status;

        public DeviceViewModel()
        {
            this.di = null;
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };
        public DeviceInformation DI
        {
            get { return this.di; }
            set
            {
                this.di = value;
                this.OnPropertyChanged();
            }
        }

        public string Status
        {
            get { return this.status; }
            set
            {
                this.status = value;
                this.OnPropertyChanged();
            }
        }

        public string DeviceDetails(DeviceInformation di)
        {
            if (di != null)
                return di.Name + " " + di.Id;
            else
                return "";
        }
        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Raise the PropertyChanged event, passing the name of the property whose value has changed.
            this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed partial class MainPage : Page
    {

        private string[] signalStrengthConversion ={"-113 dbm, Terrible", "-111dbm, Terrible"
    , "-109dbm, Marginal", "-107dbm, Marginal", "-105dbm, Marginal", "-103dbm, Marginal", "-101dbm, Marginal", "-99dbm, Marginal", "-97dbm, Marginal", "-95dbm, Marginal"
    ,"-93dbm, OK","-91dbm, OK","-89dbm, OK","-87dbm, OK","-85dbm, OK"
    ,"-83dbm, Good","-81dbm, Good","-79dbm, Good","-77dbm, Good","-75dbm, Good"
    ,"-73dbm, Excellent","-71dbm, Excellent","-69dbm, Excellent","-67dbm, Excellent","-65dbm, Excellent","-63dbm, Excellent","-61dbm, Excellent","-59dbm, Excellent","-57dbm, Excellent","-55dbm, Excellent","-53dbm, Excellent","-51dbm, Excellent"};

        private AtCommandScript[] ATComandList =
                new AtCommandScript[]{
                new AtCommandScript() { atCommand = "xxx" ,successString="OK\r\n", failString = "", preCommandDelay = 0, postCommandDelay = 0  }
                };

        private DataReader dataReader = null;

        private DataWriter dataWriter = null;

        private DeviceInformation di;

        private DeviceInformationCollection listOfDevices;

              private ILogger log = LogManagerFactory.DefaultLogManager.GetLogger<MainPage>();

        private CancellationTokenSource ReadCancellationTokenSource;
        private SerialDevice serialPort = null;
        public MainPage()
        {
            this.InitializeComponent();
            this.ViewModel = new DeviceViewModel();
        }

        public DeviceViewModel ViewModel { get; set; }
        //This method will be called by the application framework when the page is first loaded
        protected override async void OnNavigatedTo(NavigationEventArgs navArgs)
        {
            var dueTime = TimeSpan.FromSeconds(1);
            var interval = TimeSpan.FromSeconds(2);

            this.log.Info("Startup");

            //Get the port we want.
            listOfDevices = await ListAvailablePorts();
            if (listOfDevices.Count != 1)
            {
                log.Error("PI Serial Port Error.  Number of ports detected: " + listOfDevices.Count);
            }
            else
            {
                this.ViewModel.DI = listOfDevices[0];
                log.Info(this.ViewModel.DI.Name);
            }

            await Loop(interval);
        }

        private void Button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Start listening on the serial port input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task ConfigurePortAsync(DeviceInformation di)
        {
            try
            {
                serialPort = await SerialDevice.FromIdAsync(di.Id);
                if (serialPort == null) return;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(2000);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(20000);
                serialPort.BaudRate = 9600;
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Display configured settings
                this.ViewModel.Status = "Serial port: ";
                this.ViewModel.Status += serialPort.BaudRate + "-";
                this.ViewModel.Status += serialPort.DataBits + "-";
                this.ViewModel.Status += serialPort.Parity.ToString() + "-";
                this.ViewModel.Status += serialPort.StopBits;

                //Create streamwriter
                dataWriter = new DataWriter(serialPort.OutputStream);
                dataReader = new DataReader(serialPort.InputStream) { InputStreamOptions = InputStreamOptions.None };

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
            }
        }

        /// <summary>
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async Task<string> GetModemResponseAsync()
        {
            string bytesRead = string.Empty;

            try
            {
                if (serialPort != null)
                {
                    // dataReader = new DataReader(serialPort.InputStream);

                    // keep reading the serial input
                    bytesRead = await ReadAsync(256, ReadCancellationTokenSource.Token);
                    return bytesRead;
                }
                return String.Empty;
            }
            catch (TaskCanceledException tce)
            {
                this.ViewModel.Status = "Reading task was cancelled, closing device and cleaning up";
                this.ViewModel.Status = tce.Message;
                CloseDevice();
                throw tce;
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                throw ex;
            }
            finally
            {
                // Cleanup once complete
                //if (dataReaderObject != null)
                //{
                //    dataReaderObject.DetachStream();
                //    dataReaderObject = null;

                //}
            }
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async Task<DeviceInformationCollection> ListAvailablePorts()
        {
            try
            {
                string aqs = "";
                #if PI
                aqs = SerialDevice.GetDeviceSelector();

                #else
                ushort vid = 0x067B;
                ushort pid = 0x2303;
                aqs = SerialDevice.GetDeviceSelectorFromUsbVidPid(vid, pid);

                #endif
                DeviceInformationCollection dic = await DeviceInformation.FindAllAsync(aqs);

                return dic;
                //for (int i = 0; i < dic.Count; i++)
                //{
                //    listOfDevices.Add(dic[i]);
                //}
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                return null;
            }
        }

        private async Task Loop(TimeSpan interval)
        {
            //Connect to the port
            await ConfigurePortAsync(this.ViewModel.DI);

            //Select BCM2836 or BCM2837 device on PI, just to make sure....

            // TODO: Add a CancellationTokenSource and supply the token here instead of None.
            //await RunPeriodicAsync(OnTick, dueTime, interval, CancellationToken.None);

            // Repeat this loop until cancelled.
            while (true)
            {
                Task<bool> awaitable;
                bool success = false;

                try
                {
                    // Call our onTick function.
                    awaitable = OnTickAsync();

                    // Wait to repeat again.
                    await Task.Delay(interval);

                    success = await awaitable;

                    if (!success)
                        await ResetModemAsync();
                }
                catch (Exception ex)
                {
                    await ResetModemAsync();
                }
            }
        }
        private async Task<bool> OnTickAsync()
        {
            bool ok = false;
            string atComand;
            string response;
            string ss;
            string jsonData;
            modemResponse signalStrength= new modemResponse();
            int[] signal;

            string[] commands = {
                @"AT+CMEE=2",
                @"AT+SAPBR=3,1,""CONTYPE"",""GPRS""",
                @"AT+SAPBR=3,1,""APN"",""globaldata""",    //giffgaff.com
                @"AT+SAPBR=2,1",   //Query GPRS context
                @"AT+SAPBR=1,1",   //Open GPRS context
                @"AT+SAPBR=2,1",   //Query GPRS context
                @"AT+HTTPINIT",
                @"AT+HTTPSSL=1",
                @"AT+HTTPPARA=""CID"",1",
                @"AT+HTTPPARA=""URL"", ""https://pflfunctions.azurewebsites.net/api/LevelMeasurement1?code=1PFxb4FHlHKcRWqWM91lPXtgtIJLl/l3F/52V7LFVHyN2gCxQJRApw==&clientId=default""",
                @"AT+HTTPPARA=""CONTENT"",""application/json""" };
            try
            {

                //Get signal strength
                atComand = @"AT+CSQ";
                await WriteWithLogAsync(atComand);
                signalStrength = await WaitForResponseAsync(@".+OK\s{2,}", @".+CME ERROR:\s{2,}");        //Parse for ok:  "OK\r\n" and  fail:  "CME ERROR: operation not allowed\r\n"

                //Extract signal strength value
                Regex r = new Regex(@"(?:.+\+CSQ:)(\s*[\d]{1,2},[\d]{1,2}).+");
                Match match = r.Match(signalStrength.responseString);
                ss = match.Groups[1].Value.Trim();  //retrieve dta as "nn,mm" value
   
signal = ss.Split(',').Select(x => int.Parse(x)).ToArray<int>();
                jsonData = $"{{\"SignalStrength\":\"({ss}) {signalStrengthConversion[signal[0]]}\"}}";

                foreach (string command in commands)
                {
                    await WriteWithLogAsync(command);
                    ok = await WaitForResponseAsync();
                    log.Info("Modem Response to command : " + command + " is " + ok.ToString());
                    this.ViewModel.Status = "Modem Response to command : " + command + " is " + ok.ToString();
                    if (!ok)
                    {
                       return false;
                    }
                }

                //Set up download
                atComand = $"AT+HTTPDATA={jsonData.Length.ToString()},60000";   //Data length is length of json data object, in this case signal strength string
                await WriteWithLogAsync(atComand);
                ok = await WaitForResponseAsync(@"DOWNLOAD");

                //Write data    
                await WriteWithLogAsync(jsonData); // + "\u001A");
                log.Info("Writing data: " + jsonData);
                //wait for echo
                response = await GetModemResponseAsync();

                //POST command
                atComand = @"AT+HTTPACTION=1";
                await WriteWithLogAsync(atComand);
                //check for HTTP OK (200)
                ok = await WaitForResponseAsync(@",200,");
                log.Info("Modem Response to command : " + atComand + " is " + ok.ToString());
                this.ViewModel.Status = "Modem Response to command : " + atComand + " is " + ok.ToString();

                //close HTTP session
                atComand = @"AT+HTTPTERM";
                await WriteWithLogAsync(atComand);
                ok = await WaitForResponseAsync();
                log.Info("Modem Response to command : " + atComand + " is " + ok.ToString());
                this.ViewModel.Status = "Modem Response to command : " + atComand + " is " + ok.ToString();

                //Close GPRS context
                atComand = @"AT+SAPBR=0,1";
                await WriteWithLogAsync(atComand);
                ok = await WaitForResponseAsync();
                log.Info("Modem Response to command : " + atComand + " is " + ok.ToString());
                this.ViewModel.Status = "Modem Response to command : " + atComand + " is " + ok.ToString();

                return true;
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                throw ex;
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<string> ReadAsync(uint bufferLength, CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = bufferLength;
            string bytesRead;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            // dataReader.InputStreamOptions = InputStreamOptions.Partial;

            using (var childCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // Create a task object to wait for data on the serialPort.InputStream
                loadAsyncTask = dataReader.LoadAsync(ReadBufferLength).AsTask(childCancellationTokenSource.Token);

                // Launch the task and wait
                UInt32 bytesReadCount = await loadAsyncTask;
                if (bytesReadCount > 0)
                {
                    bytesRead = dataReader.ReadString(bytesReadCount);
                    bytesRead = bytesRead.Replace("\r", @"\r");
                    bytesRead = bytesRead.Replace("\n", @"\n");
                    this.ViewModel.Status = "Bytes read: " + bytesRead;
                    log.Info("Bytes read: " + bytesRead);
                    //dataReader.DetachStream();
                    return bytesRead;
                }
                else
                {
                    this.ViewModel.Status = "No bytes read";
                    return string.Empty;
                }
            }
        }

        private async Task ResetModemAsync()
        {
            string s;
            bool ok;

            CloseDevice();
            await ConfigurePortAsync(this.ViewModel.DI);

            //Reboot and wait to restart
            await WriteWithLogAsync("AT+CPOWD=1");
            await WaitForResponseAsync();
            await Task.Delay(20000);

            //Reset
            await WriteWithLogAsync("AT+CFUN=0");
            await WaitForResponseAsync();
            await WriteWithLogAsync("AT+CFUN=1");
            ok = await WaitForResponseAsync(@"Call Ready");

            //Try resetting HTTP
            //await WriteWithLogAsync("AT+HTTPTERM");
            //await WaitForOKResponseAsync();

            ////Close GPRS context
            //await WriteWithLogAsync(@"AT+SAPBR=0,1");
            //await WaitForOKResponseAsync();
        }

        private async Task<bool> WaitForResponseAsync()
        {
            try
            {
                if (await WaitForResponseAsync(@"OK\r\n"))
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                throw ex;
            }
        }

        private async Task<bool> WaitForResponseAsync(string s)
        {
            try
            {
                string response = await GetModemResponseAsync();
                if (response.Contains(s))
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                throw ex;
            }
        }

        private async Task<modemResponse> WaitForResponseAsync(string successMatch, string failMatch)
        {

            Regex successR = new Regex(successMatch);
            Regex errorR = new Regex(failMatch);
            modemResponse r = new modemResponse();

            try
            {
                r.responseString = await GetModemResponseAsync();
             
                if (successR.Match(r.responseString).Success)
                    r.responseStatus =  aTCommandResponse.ok;
                else if (errorR.Match(   r.responseString).Success)
                    r.responseStatus = aTCommandResponse.error;
                else
                    r.responseStatus = aTCommandResponse.unknown;
                return r;
            }
            catch (Exception ex)
            {
                this.log.Error(ex.Message);
                this.ViewModel.Status = ex.Message;
                throw ex;
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync(string data)
        {
            Task<UInt32> storeAsyncTask;

            using (DataWriter dataWriter = new DataWriter(serialPort.OutputStream))
            {
                if (data.Length != 0)
                {
                    // Load the text from the sendText input text box to the dataWriter object
                    dataWriter.WriteString(data + "\r\n");

                    // Launch an async task to complete the write operation
                    storeAsyncTask = dataWriter.StoreAsync().AsTask();

                    UInt32 bytesWritten = await storeAsyncTask;
                    //await dataWriter.FlushAsync();
                    dataWriter.DetachStream();
                    if (bytesWritten > 0)
                    {
                        this.ViewModel.Status = "At " + DateTime.Now.ToLocalTime() + " " + data + " ";
                        this.ViewModel.Status += " written";
                    }
                }
                else
                {
                    this.ViewModel.Status = "No text";
                }
            }
        }
        private async Task WriteWithLogAsync(string command)
        {
            log.Info("Before AT write command");
            await WriteAsync(command);
            log.Info("After Writing AT command: " + command);
        }
        //private void Initialize()
        //{
        //    var dueTime = TimeSpan.FromSeconds(5);
        //    var interval = TimeSpan.FromSeconds(5);

        //    // TODO: Add a CancellationTokenSource and supply the token here instead of None.
        //    RunPeriodicAsync(OnTick, dueTime, interval, CancellationToken.None);
        //}
    }
}