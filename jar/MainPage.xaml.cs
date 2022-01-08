using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using OxyPlot;
using OxyPlot.Series;
using Windows.UI.ViewManagement;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Collections.ObjectModel;

// 空白ページの項目テンプレートについては、https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x411 を参照してください

namespace jar
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private BluetoothLEDevice bluetoothLeDevice = null;
        private GattCharacteristic commandCharacteristic = null;
        private GattCharacteristic statusCharacteristic = null;
        private LineSeries temperatureSeries = null;
        private DispatcherTimer devicePoller = null;
        private int sampleCount = 0;
        private FileStream deviceLogFile = null;
        private StreamWriter deviceLog = null;

        #region Error Codes
        readonly int E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED = unchecked((int)0x80650003);
        readonly int E_BLUETOOTH_ATT_INVALID_PDU = unchecked((int)0x80650004);
        readonly int E_ACCESSDENIED = unchecked((int)0x80070005);
        readonly int E_DEVICE_NOT_AVAILABLE = unchecked((int)0x800710df); // HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE)
        #endregion

        private readonly Guid GattServiceUuid = new Guid("5C8BF11D-6BA4-4225-9EF3-3996C366D06E");
        private readonly Guid GattCommandCharacteristicUuid = new Guid("EE697780-185C-4A62-BA71-F7DDBB0AB33B");
        private readonly Guid GattStatusCharacteristicUuid = new Guid("015C7F86-4C72-73AC-0933-7967B1238736");


        public PlotModel DeviceStatusModel { get; set; }

        public BluetoothLEDevice ConnectedBLEDevice
		{
			get { return (BluetoothLEDevice)GetValue(ConnectedBLEDeviceProperty); }
			set { SetValue(ConnectedBLEDeviceProperty, value); }
		}

		// Using a DependencyProperty as the backing store for ConnectedBLEDevice.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty ConnectedBLEDeviceProperty =
			DependencyProperty.Register("ConnectedBLEDevice", typeof(BluetoothLEDevice), typeof(MainPage), new PropertyMetadata(null));

		public int StatusCommand
		{
			get { return (int)GetValue(StatusCommandProperty); }
			set { SetValue(StatusCommandProperty, value); }
		}

		// Using a DependencyProperty as the backing store for StatusCommand.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty StatusCommandProperty =
			DependencyProperty.Register("StatusCommand", typeof(int), typeof(MainPage), new PropertyMetadata(0));

		public int StatusCommandIndex
		{
			get { return (int)GetValue(StatusCommandIndexProperty); }
			set { SetValue(StatusCommandIndexProperty, value); }
		}

		// Using a DependencyProperty as the backing store for StatusCommandIndex.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty StatusCommandIndexProperty =
			DependencyProperty.Register("StatusCommandIndex", typeof(int), typeof(MainPage), new PropertyMetadata(0));

		public int StatusCommandNum
		{
			get { return (int)GetValue(StatusCommandNumProperty); }
			set { SetValue(StatusCommandNumProperty, value); }
		}

		// Using a DependencyProperty as the backing store for StatusCommandNum.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty StatusCommandNumProperty =
			DependencyProperty.Register("StatusCommandNum", typeof(int), typeof(MainPage), new PropertyMetadata(0));

		public int StatusPowerRate
		{
			get { return (int)GetValue(StatusPowerRateProperty); }
			set { SetValue(StatusPowerRateProperty, value); }
		}

		// Using a DependencyProperty as the backing store for StatusPowerRate.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty StatusPowerRateProperty =
			DependencyProperty.Register("StatusPowerRate", typeof(int), typeof(MainPage), new PropertyMetadata(0));

		public double StatusTemperature
		{
			get { return (double)GetValue(StatusTemperatureProperty); }
			set { SetValue(StatusTemperatureProperty, value); }
		}

		// Using a DependencyProperty as the backing store for StatusTemperature.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty StatusTemperatureProperty =
			DependencyProperty.Register("StatusTemperature", typeof(double), typeof(MainPage), new PropertyMetadata(0.0));

		public int StatusLeftTime
		{
			get { return (int)GetValue(StatusLeftTimeProperty); }
			set { SetValue(StatusLeftTimeProperty, value); }
		}

        // Using a DependencyProperty as the backing store for StatusLeftTime.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty StatusLeftTimeProperty =
			DependencyProperty.Register("StatusLeftTime", typeof(int), typeof(MainPage), new PropertyMetadata(0));

        private ObservableCollection<CommandDataDisplay> Commands = new ObservableCollection<CommandDataDisplay>();

        public MainPage()
        {
            this.InitializeComponent();
            ApplicationView.PreferredLaunchViewSize = new Size(800, 400);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
            ApplicationView.GetForCurrentView().TryResizeView(new Size(800, 400));

            temperatureSeries = new LineSeries();
            temperatureSeries.Title = "温度";
            DeviceStatusModel = new PlotModel() { Title = "デバイス状態" };
            DeviceStatusModel.Series.Add(temperatureSeries);

            devicePoller = new DispatcherTimer();
            devicePoller.Interval = TimeSpan.FromSeconds(1);
			devicePoller.Tick += DevicePoller_Tick;

            for(int i = 0; i < 32; ++i)
			{
                var command = new CommandDataDisplay();
                command.Index = i;
				command.PropertyChanged += Command_PropertyChanged;
                Commands.Add(command);
            }

            var appFolder = Windows.Storage.ApplicationData.Current.RoamingFolder.Path;
            var logPath = Path.Combine(appFolder, DateTime.Now.ToString("yyyyMMddHHmmss") + ".csv");
            deviceLogFile = File.Open(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            deviceLog = new StreamWriter(deviceLogFile);
        }

		private async void Command_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
            if (e.PropertyName == "VisibleTemperature" 
                || e.PropertyName == "VisibleMinute"
                || e.PropertyName == "VisibleKp"
                || e.PropertyName == "VisibleTi"
                || e.PropertyName == "VisiblePhaseDelay"
                || e.PropertyName == "VisiblePower")
                return;

            var command = sender as CommandDataDisplay;
            if (command == null)
                return;

            var bytes = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
            bytes[0] = (byte)command.Type;
            bytes[1] = (byte)command.Index;

            switch(command.Type)
			{
                case CommandDataDisplay.Command.TargetTemperature:
                    bytes[2] = (byte)command.Temperature;
                    break;
                case CommandDataDisplay.Command.Keep:
                    BitConverter.GetBytes((ushort)command.Minute).CopyTo(bytes, 2);
                    break;
                case CommandDataDisplay.Command.SetKp:
                    BitConverter.GetBytes(command.Kp).CopyTo(bytes, 2);
                    break;
                case CommandDataDisplay.Command.SetTi:
                    BitConverter.GetBytes(command.Ti).CopyTo(bytes, 2);
                    break;
                case CommandDataDisplay.Command.SetPhaseDelay:
                    BitConverter.GetBytes((ushort)command.PhaseDelay).CopyTo(bytes, 2);
                    break;
                case CommandDataDisplay.Command.SetPower:
                    BitConverter.GetBytes((ushort)command.Power).CopyTo(bytes, 2);
                    break;
            }

            await WriteBufferToSelectedCharacteristicAsync(bytes.AsBuffer());
		}

		private void DevicePoller_Tick(object sender, object e)
		{
            devicePoller.Stop();
            if (bluetoothLeDevice == null || bluetoothLeDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                BLEDeviceListFlyout.ShowAt(TopGrid);
		}

		private async void BLEDeviceListFlyout_Closed(object sender, object e)
		{
            ConnectedBLEDevice = null;
            if (!await ClearBluetoothLEDeviceAsync())
            {
                Debug.WriteLine("Error: Unable to reset state, try again.");
            }

            var deviceId = BLEDeviceList.SelectDeviceId;              
            if(!string.IsNullOrEmpty(deviceId) && await ConnectToDevice(deviceId))
			{
                ConnectedBLEDevice = bluetoothLeDevice;
				bluetoothLeDevice.ConnectionStatusChanged += BluetoothLeDevice_ConnectionStatusChanged;
            }           
            else
            {
                devicePoller.Start();
            }
        }

		private async void BluetoothLeDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
		{
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
			{
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    BLEDeviceListFlyout.ShowAt(TopGrid);
                });
			}
        }

		private async Task<bool> ConnectToDevice(string deviceId)
		{
            try
            {
                try
                {
                    // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                    bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(deviceId);

                    if (bluetoothLeDevice == null)
                    {
                        throw new ApplicationException("Failed to connect to device.");
                    }
                }
                catch (Exception ex) when (ex.HResult == E_DEVICE_NOT_AVAILABLE)
                {
                    throw new ApplicationException("Bluetooth radio is not on.");
                }

                GattDeviceService deviceService = null;

                // Note: BluetoothLEDevice.GattServices property will return an empty list for unpaired devices. For all uses we recommend using the GetGattServicesAsync method.
                // BT_Code: GetGattServicesAsync returns a list of all the supported services of the device (even if it's not paired to the system).
                // If the services supported by the device are expected to change during BT usage, subscribe to the GattServicesChanged event.
                GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    var services = result.Services;
                    Debug.WriteLine(String.Format("Found {0} services", services.Count));
                    foreach (var service in services)
                    {
                        if (service.Uuid == GattServiceUuid)
                        {
                            deviceService = service;
                            break;
                        }
                    }
                }
                else
                {
                    throw new ApplicationException("Device unreachable");
                }

                if (deviceService == null)
                {
                    throw new ApplicationException("Service not found.");
                }

                var characteristicResult = await deviceService.GetCharacteristicsForUuidAsync(GattCommandCharacteristicUuid);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    commandCharacteristic = characteristicResult.Characteristics[0];
                }
                else
                {
                    throw new ApplicationException("Characteristic not found.");
                }

                characteristicResult = await deviceService.GetCharacteristicsForUuidAsync(GattStatusCharacteristicUuid);
                if (result.Status == GattCommunicationStatus.Success)
                {
                    statusCharacteristic = characteristicResult.Characteristics[0];
                    var status = await statusCharacteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    if (status.Status == GattCommunicationStatus.Success)
                    {
                        statusCharacteristic.ValueChanged += Characteristic_ValueChanged;
                    }
                    else
                    {
                        throw new ApplicationException("Invalid characteristic property.");
                    }
                }
                else
				{
                    throw new ApplicationException("Characteristic not found.");
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
                await ClearBluetoothLEDeviceAsync();
                return false;
            }

            return true;
        }

		private async Task<bool> ClearBluetoothLEDeviceAsync()
        {
            try
            {
                if (statusCharacteristic != null)
                {
                    // Need to clear the CCCD from the remote device so we stop receiving notifications
                    var result = await statusCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    if (result != GattCommunicationStatus.Success)
                    {
                        return false;
                    }
                    else
                    {
                        statusCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                    }
                }
            }
            catch (System.ObjectDisposedException)
            {
                Debug.WriteLine("Device disposed already.");
            }
            statusCharacteristic = null;
            commandCharacteristic = null;
            bluetoothLeDevice?.Dispose();
            bluetoothLeDevice = null;
            return true;
        }

		private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
            var buffer = args.CharacteristicValue;
            var data = new byte[8] { 0, 0, 0, 0, 0, 0, 0, 0 };

            for (int i = 0; i < Math.Min(buffer.Length, 8); ++i)
                data[8 - i - 1] = buffer.GetByte((uint)(buffer.Length - i - 1));

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                StatusCommand = data[0];
                StatusCommandIndex = data[1];
                StatusCommandNum = data[2];
                StatusPowerRate = data[3];
                StatusTemperature = (short)(data[4] | (data[5] << 8)) / 256.0;
                StatusLeftTime = data[6] | (data[7] << 8);

                if (deviceLog != null)
                {
                    deviceLog.WriteLine($"{DateTime.Now},{StatusCommand},{StatusCommandIndex},{StatusCommandNum},{StatusPowerRate},{StatusTemperature}");
                    deviceLog.Flush();
                }

                temperatureSeries.Points.Add(new DataPoint(sampleCount++, StatusTemperature));
                if (temperatureSeries.Points.Count > 100)
                    temperatureSeries.Points.RemoveAt(0);

                DeviceStatusModel.InvalidatePlot(true);
            });
        }

		private async Task<bool> WriteBufferToSelectedCharacteristicAsync(IBuffer buffer)
        {
            try
            {
                // BT_Code: Writes the value from the buffer to the characteristic.
                var result = await commandCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    Debug.WriteLine("Successfully wrote value to device");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"Write failed: {result.Status}");
                    return false;
                }
            }
            catch (Exception ex) when (ex.HResult == E_BLUETOOTH_ATT_INVALID_PDU)
            {
                Debug.WriteLine(ex.Message);
                return false;
            }
            catch (Exception ex) when (ex.HResult == E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED || ex.HResult == E_ACCESSDENIED)
            {
                // This usually happens when a device reports that it support writing, but it actually doesn't.
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

		private void page_Loaded(object sender, RoutedEventArgs e)
		{
            BLEDeviceListFlyout.ShowAt(TopGrid);
		}
	}
}
