using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.UI.Core;
using System.Diagnostics;
using SDKTemplate;

// ユーザー コントロールの項目テンプレートについては、https://go.microsoft.com/fwlink/?LinkId=234236 を参照してください

namespace jar
{
	public sealed partial class BLEDeviceList : UserControl
	{
		private ObservableCollection<BluetoothLEDeviceDisplay> KnownDevices = new ObservableCollection<BluetoothLEDeviceDisplay>();
		private List<DeviceInformation> UnknownDevices = new List<DeviceInformation>();

		private DeviceWatcher deviceWatcher;

        public string SelectDeviceId { get; private set; }


		public BLEDeviceList()
		{
			this.InitializeComponent();
		}

        private void StartBleDeviceWatcher()
        {
            // Additional properties we would like about the device.
            // Property strings are documented here https://msdn.microsoft.com/en-us/library/windows/desktop/ff521659(v=vs.85).aspx
            string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };

            // BT_Code: Example showing paired and non-paired in a single query.
            string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            deviceWatcher =
                    DeviceInformation.CreateWatcher(
                        aqsAllBluetoothLEDevices,
                        requestedProperties,
                        DeviceInformationKind.AssociationEndpoint);

            // Register event handlers before starting the watcher.
            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;
            deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            deviceWatcher.Stopped += DeviceWatcher_Stopped;

            // Start over with an empty collection.
            KnownDevices.Clear();

            // Start the watcher. Active enumeration is limited to approximately 30 seconds.
            // This limits power usage and reduces interference with other Bluetooth activities.
            // To monitor for the presence of Bluetooth LE devices for an extended period,
            // use the BluetoothLEAdvertisementWatcher runtime class. See the BluetoothAdvertisement
            // sample for an example.
            deviceWatcher.Start();
        }

        /// <summary>
        /// Stops watching for all nearby Bluetooth devices.
        /// </summary>
        private void StopBleDeviceWatcher()
        {
            if (deviceWatcher != null)
            {
                // Unregister the event handlers.
                deviceWatcher.Added -= DeviceWatcher_Added;
                deviceWatcher.Updated -= DeviceWatcher_Updated;
                deviceWatcher.Removed -= DeviceWatcher_Removed;
                deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
                deviceWatcher.Stopped -= DeviceWatcher_Stopped;

                // Stop the watcher.
                deviceWatcher.Stop();
                deviceWatcher = null;
            }
        }

        private BluetoothLEDeviceDisplay FindBluetoothLEDeviceDisplay(string id)
        {
            foreach (BluetoothLEDeviceDisplay bleDeviceDisplay in KnownDevices)
            {
                if (bleDeviceDisplay.Id == id)
                {
                    return bleDeviceDisplay;
                }
            }
            return null;
        }

        private DeviceInformation FindUnknownDevices(string id)
        {
            foreach (DeviceInformation bleDeviceInfo in UnknownDevices)
            {
                if (bleDeviceInfo.Id == id)
                {
                    return bleDeviceInfo;
                }
            }
            return null;
        }

        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                lock (this)
                {
                    Debug.WriteLine(String.Format("Added {0}{1}", deviceInfo.Id, deviceInfo.Name));

                    // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                    if (sender == deviceWatcher)
                    {
                        // Make sure device isn't already present in the list.
                        if (FindBluetoothLEDeviceDisplay(deviceInfo.Id) == null)
                        {
                            if (deviceInfo.Name != string.Empty)
                            {
                                // If device has a friendly name display it immediately.
                                KnownDevices.Add(new BluetoothLEDeviceDisplay(deviceInfo));
                            }
                            else
                            {
                                // Add it to a list in case the name gets updated later. 
                                UnknownDevices.Add(deviceInfo);
                            }
                        }

                    }
                }
            });
        }

        private async void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                lock (this)
                {
                    Debug.WriteLine(String.Format("Updated {0}{1}", deviceInfoUpdate.Id, ""));

                    // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                    if (sender == deviceWatcher)
                    {
                        BluetoothLEDeviceDisplay bleDeviceDisplay = FindBluetoothLEDeviceDisplay(deviceInfoUpdate.Id);
                        if (bleDeviceDisplay != null)
                        {
                            // Device is already being displayed - update UX.
                            bleDeviceDisplay.Update(deviceInfoUpdate);
                            return;
                        }

                        DeviceInformation deviceInfo = FindUnknownDevices(deviceInfoUpdate.Id);
                        if (deviceInfo != null)
                        {
                            deviceInfo.Update(deviceInfoUpdate);
                            // If device has been updated with a friendly name it's no longer unknown.
                            if (deviceInfo.Name != String.Empty)
                            {
                                KnownDevices.Add(new BluetoothLEDeviceDisplay(deviceInfo));
                                UnknownDevices.Remove(deviceInfo);
                            }
                        }
                    }
                }
            });
        }

        private async void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                lock (this)
                {
                    Debug.WriteLine(String.Format("Removed {0}{1}", deviceInfoUpdate.Id, ""));

                    // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                    if (sender == deviceWatcher)
                    {
                        // Find the corresponding DeviceInformation in the collection and remove it.
                        BluetoothLEDeviceDisplay bleDeviceDisplay = FindBluetoothLEDeviceDisplay(deviceInfoUpdate.Id);
                        if (bleDeviceDisplay != null)
                        {
                            KnownDevices.Remove(bleDeviceDisplay);
                        }

                        DeviceInformation deviceInfo = FindUnknownDevices(deviceInfoUpdate.Id);
                        if (deviceInfo != null)
                        {
                            UnknownDevices.Remove(deviceInfo);
                        }
                    }
                }
            });
        }

        private async void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object e)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    //rootPage.NotifyUser($"{KnownDevices.Count} devices found. Enumeration completed.",
                    //    NotifyType.StatusMessage);
                }
            });
        }

        private async void DeviceWatcher_Stopped(DeviceWatcher sender, object e)
        {
            // We must update the collection on the UI thread because the collection is databound to a UI element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Protect against race condition if the task runs after the app stopped the deviceWatcher.
                if (sender == deviceWatcher)
                {
                    //rootPage.NotifyUser($"No longer watching for devices.",
                    //        sender.Status == DeviceWatcherStatus.Aborted ? NotifyType.ErrorMessage : NotifyType.StatusMessage);
                }
            });
        }

		private void UserControl_Loaded(object sender, RoutedEventArgs e)
		{
            StartBleDeviceWatcher();
        }

		private void UserControl_Unloaded(object sender, RoutedEventArgs e)
		{
            StopBleDeviceWatcher();
        }

		private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
            var item = ((ListView)sender).SelectedItem as BluetoothLEDeviceDisplay;
            if (item != null)
            {
                SelectDeviceId = item.Id;
            }
        }
	}
}
