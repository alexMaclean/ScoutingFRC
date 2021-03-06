using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Android.Bluetooth;
using System.Diagnostics;
using Android.Content.PM;

namespace ScoutingFRC
{
    [Activity(Label = "Sync Data", ScreenOrientation = ScreenOrientation.Portrait)]
    public class SyncDataActivity : Activity
    {
        private List<TeamData> currentData;
        private List<TeamData> newData;

        private BluetoothCallbacks<BluetoothConnection> callbacks;

        private ArrayAdapter<string> adapter;
        private List<BluetoothDevice> bluetoothDevices;
        private BluetoothAdapter bluetoothAdapter;
        private BluetoothService bs;

        private class BluetoothDataTransfer
        {
            public BluetoothDataTransfer(BluetoothConnection connection = null, BluetoothDevice device = null, bool weStarted = false)
            {
                this.device = device;
                this.connection = connection;
                this.weStarted = weStarted;
                done = false;
            }

            public BluetoothDevice device;
            public BluetoothConnection connection;
            public bool weStarted;
            public bool done;
        }

        private List<BluetoothDataTransfer> btDataTransfers;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.SyncDevices);

            var bytes = Intent.GetByteArrayExtra("currentData");
            currentData = MatchData.Deserialize<List<TeamData>>(bytes);
            FindViewById<Button>(Resource.Id.buttonAdd).Click += ButtonAdd_Click;
            FindViewById<Button>(Resource.Id.buttonCancel).Click += ButtonCancel_Click;
            FindViewById<ListView>(Resource.Id.listViewDevices).ItemClick += SyncDataActivity_ItemClick;

            callbacks = new BluetoothCallbacks<BluetoothConnection>();
            callbacks.error = ErrorCallback;
            callbacks.dataReceived = DataCallback;
            callbacks.dataSent = DataSentCallback;
            callbacks.connected = ConnectedCallback;
            callbacks.disconnected = DisconnectedCallback;

            newData = new List<TeamData>();

            btDataTransfers = new List<BluetoothDataTransfer>();

            adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSelectableListItem);
            var listView = FindViewById<ListView>(Resource.Id.listViewDevices);
            listView.Adapter = adapter;

            var bluetoothReceiver = new BluetoothReceiver(DiscoveryFinished, DeviceDiscovered);
            RegisterReceiver(bluetoothReceiver, new IntentFilter(BluetoothAdapter.ActionDiscoveryFinished));
            RegisterReceiver(bluetoothReceiver, new IntentFilter(BluetoothDevice.ActionFound));

            bluetoothDevices = new List<BluetoothDevice>();

            bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
            if (bluetoothAdapter != null) {
                if (!bluetoothAdapter.IsEnabled) {
                    bluetoothAdapter.Enable();

                    for (int i = 0; i < 100 && !bluetoothAdapter.IsEnabled; ++i) {
                        System.Threading.Thread.Sleep(10);
                    }

                    if(!bluetoothAdapter.IsEnabled) {
                        Toast.MakeText(this, "Bluetooth is disabled and can't be automatically enabled.", ToastLength.Long).Show();
                        return;
                    }
                }
                 
                if (bluetoothAdapter.ScanMode != ScanMode.ConnectableDiscoverable) {
                    Intent discoverableIntent = new Intent(BluetoothAdapter.ActionRequestDiscoverable);
                    discoverableIntent.PutExtra(BluetoothAdapter.ExtraDiscoverableDuration, 60);
                    StartActivity(discoverableIntent);
                }

                bs = new BluetoothService(this, callbacks, bluetoothAdapter);
                SearchForDevices();
            }
            else {
                Toast.MakeText(this, "Bluetooth not supported on this device.", ToastLength.Long).Show();
            }
        }
        
        /// <summary>  
        ///  Button Listner for the SyncData Button, displays statu and starts syncing
        /// </summary> 
        private void SyncDataActivity_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            BluetoothDevice device = bluetoothDevices[(int)(e.Id)];

            lock (bs.connectionsLock) {
                if (bs.connections.Any(_bc => _bc.bluetoothDevice.Address == device.Address)) {
                    Toast.MakeText(this, "Already connected to this device. Disconnecting", ToastLength.Long).Show();
                    bs.Disconnect(device);
                }
                else if (bs.connections.Count > 0) {
                    Toast.MakeText(this, "Already connected to a device.", ToastLength.Long).Show();
                }
                else {
                    btDataTransfers.Add(new BluetoothDataTransfer(null, device, true));
                    bs.Connect(device);  
                }
            }
        }
        
        /// <summary>  
        /// Gets called when a new device is discovered.
        /// </summary> 
        private void DeviceDiscovered(BluetoothDevice device)
        {
            bool bonded = device.BondState == Bond.Bonded;
            int insertIndex = bonded ? 0 : bluetoothDevices.Count;

            bluetoothDevices.Insert(insertIndex, device);
            
            adapter.Insert((bonded ? "Paired: " : "") + FormatDeviceName(device), insertIndex);
            adapter.NotifyDataSetChanged();
        }
        
        /// <summary>  
        ///  Gets called when the discovery finishes.
        /// </summary> 
        private void DiscoveryFinished(List<BluetoothDevice> devices)
        {        
        }
        
        /// <summary>  
        ///  Starts searching for bluetooth devices.
        /// </summary> 
        private void SearchForDevices()
        {
            if (bluetoothAdapter != null) {
                if (bluetoothAdapter.StartDiscovery()) {
                    bluetoothDevices.Clear();
                    adapter.Clear();
                    adapter.NotifyDataSetChanged();
                }
                else {
                    Debugger.Break();
                }
            }
        }
        
        /// <summary>  
        ///  Update the UI with info about completion of syncing. 
        /// </summary> 
        private void ChangeTextViews()
        {
            FindViewById<TextView>(Resource.Id.textViewReceived).Text = "Matches Received: " + newData.Count;
            FindViewById<TextView>(Resource.Id.textViewSent).Text = "Matches Sent: " + currentData.Count;

            Toast.MakeText(this, "Done", ToastLength.Long).Show();
        }
        
        /// <summary>  
        ///  Gets called when an error occurs in a bluetooth connection.
        /// </summary> 
        private void ErrorCallback(BluetoothConnection bluetoothConnection, Exception ex)
        {
            RunOnUiThread(() => {
                var btd = btDataTransfers.FirstOrDefault(bt => bt.connection == bluetoothConnection);
                if (btd == null || !btd.done) {
                    Toast.MakeText(this, "Error from " + (bluetoothConnection != null ? FormatDeviceName(bluetoothConnection.bluetoothDevice) : "BluetoothService") + ": " + ex.Message, ToastLength.Long).Show();

                }
            });
        }
        
        /// <summary>  
        ///  Gets called when data is received.
        /// </summary> 
        private void DataCallback(BluetoothConnection bluetoothConnection, byte[] data)
        {
            RunOnUiThread(() => {
                Toast.MakeText(this, "Data received", ToastLength.Short).Show();

                List<TeamData> newMatchData = MatchData.Deserialize<List<TeamData>>(data);

                foreach (var md in newMatchData) {
                    if (currentData.FindIndex(td => td.Equals(md)) < 0) {
                        newData.Add(md);
                    }
                }

                var btd = btDataTransfers.FirstOrDefault(bt => bt.connection == bluetoothConnection);

                if(btd != null) {
                    if (btd.weStarted) {
                        SendData(bluetoothConnection);
                    }
                    else {
                        ChangeTextViews();
                        bluetoothConnection.Disconnect();
                        btd.weStarted = false;
                        btd.done = true;
                    }
                }
            });
        }
        
        /// <summary>  
        ///  Gets called when data is successfully sent.
        /// </summary> 
        private void DataSentCallback(BluetoothConnection bluetoothConnection, int id)
        {
            RunOnUiThread(() => {
                Toast.MakeText(this, "Data sent", ToastLength.Short).Show();

                var btd = btDataTransfers.FirstOrDefault(bt => bt.connection == bluetoothConnection);
                if (btd != null && btd.weStarted) {
                    btd.done = true;
                }
            });
        }
        
        /// <summary>  
        /// Sends the currentData to a bluetooth connection.
        /// </summary> 
        private void SendData(BluetoothConnection bluetoothConnection)
        {
            var serialized = MatchData.Serialize(currentData);
            byte[] data = new byte[sizeof(int) + serialized.Length];
            BitConverter.GetBytes(serialized.Length).CopyTo(data, 0);
            serialized.CopyTo(data, sizeof(int));
            int id = 0;
            bluetoothConnection.Write(data, ref id);
        }
        
        /// <summary>  
        /// Neatly formats the name of a bluetooth device.
        /// </summary>  
        private string FormatDeviceName(BluetoothDevice device)
        {
            return device.Name != null ? (device.Name + " (" + device.Address + ")") : device.Address;
        }

        /// <summary>  
        /// Gets called when connected to a bluetooth device.
        /// </summary>  
        private void ConnectedCallback(BluetoothConnection bluetoothConnection)
        {
           RunOnUiThread(() => {
               Toast.MakeText(this, "Connected to " + FormatDeviceName(bluetoothConnection.bluetoothDevice), ToastLength.Short).Show();

               newData.Clear();

                var btd = btDataTransfers.FirstOrDefault(bt => bt.device == bluetoothConnection.bluetoothDevice);
                if (btd == null) {
                    btd = new BluetoothDataTransfer(bluetoothConnection, bluetoothConnection.bluetoothDevice, false);
                    btDataTransfers.Add(btd);
                }
                else if (btd.connection == null) {
                    btd.connection = bluetoothConnection;
                }

                if(!btd.weStarted) {
                    SendData(bluetoothConnection);
                }
            });
        }
        
        /// <summary>  
        /// Called when disconnected from a bluetooth device.
        /// </summary>  
        private void DisconnectedCallback(BluetoothConnection bluetoothConnection)
        {
            RunOnUiThread(() => {
                var btd = btDataTransfers.FirstOrDefault(bt => bt.connection == bluetoothConnection);
                if(btd != null) {
                    if (btd.weStarted && btd.done) {
                        ChangeTextViews();
                    }
                    else if(!btd.done){
                        Toast.MakeText(this, "Connection was interrupted", ToastLength.Long).Show();
                    }

                    btDataTransfers.Remove(btd);
                }

            });
        }
        
        /// <summary>  
        ///  When the application is closed bluetooth is stopped. 
        /// </summary>  
        protected override void OnDestroy()
        {
            bs.StopListening();
            bluetoothAdapter.CancelDiscovery();
            base.OnDestroy();
        }
        
        /// <summary>  
        ///  Cancels the Activity returning nothing.
        /// </summary>  
        private void Cancel()
        {
            Intent myIntent = new Intent(this, typeof(MainActivity));
            SetResult(Result.Canceled, myIntent);
            Finish();
        }
        
        /// <summary>  
        ///  Creates an Alerd Dialog to confirm the user wants to quit.
        /// </summary>  
        private void AlertDialogClick(object sender, DialogClickEventArgs dialogClickEventArgs)
        {
            if (dialogClickEventArgs.Which == -1)
            {
                Intent myIntent = new Intent(this, typeof(MainActivity));
                SetResult(Result.Canceled, myIntent);
                Finish();
            }
        }
        
        /// <summary>  
        ///  Button Listener for ButtonCancel, ends activity and returns nothing, after confirming with user. 
        /// </summary>  
        private void ButtonCancel_Click(object sender, EventArgs eventArgs)
        {
            if (newData.Count > 0) {
                var builder = new AlertDialog.Builder(this)
                    .SetMessage("Do you really want to cancel without adding the synced data to the database?")
                    .SetPositiveButton("Yes", AlertDialogClick)
                    .SetNegativeButton("No", AlertDialogClick);
                builder.Create().Show();
            }
            else {
                Cancel();
            }
        }
        
        /// <summary>  
        ///  Button Listener for ButtonAdd, ends activity and returns new data. 
        /// </summary>  
        private void ButtonAdd_Click(object sender, EventArgs eventArgs)
        {
            Intent myIntent = new Intent(this, typeof(MainActivity));
            var bytes = MatchData.Serialize(newData);
            myIntent.PutExtra("newMatches", bytes);
            SetResult(Result.Ok, myIntent);
            Finish();
        }
    }
}
