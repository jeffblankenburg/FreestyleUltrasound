using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace FreestyleUltrasound
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        DispatcherTimer Timer = new DispatcherTimer();
        StreamSocket socket;
        DataReader Reader;
        DataWriter Writer;
        bool IsConnected = false;
        string FileHeader = "";


        DataWriter WorkListWriter;
        StringBuilder sb = new StringBuilder();
        List<BitmapImage> currentImages = new List<BitmapImage>();
        BitmapImage currentImage = new BitmapImage();
        uint currentbytes = 0;
        byte[] imagebytearray;
        uint counter = 0;
        bool ShouldStudyListKeepLooping = true;
        bool ShouldAllImagesKeepLooping = true;
        bool IsGettingImages = false;
        DateTime TimeNow;
        
        
        public MainPage()
        {
            this.InitializeComponent();
            LoadDeviceList();
        }

        private void FreestyleFinder()
        {
            
        }

        private void Debug(string s)
        {
            DebugConsole.Text += s + "\n";
        }

        private void StartTimer()
        {
            Debug("Start Timer");
            Timer.Interval = new TimeSpan(0, 0, 0, 2);
            Timer.Tick += Timer_Tick;
            TimeNow = DateTime.Now;
            Timer.Start();
        }

        void Timer_Tick(object sender, object e)
        {
            EndTimer();
        }

        private void EndTimer()
        {
            Timer.Stop();
            Debug("Timer took " + (DateTime.Now - TimeNow).TotalSeconds.ToString() + " seconds");
        }

        private async Task ConnectToSelectedDevice()
        {
            IsConnected = false;
            Debug("Connecting");

            socket = new StreamSocket();
            string IPAddress = App.settings.Values["deviceaddress"].ToString();
            HostName hostname = new HostName(IPAddress);

            CancellationTokenSource cts = new CancellationTokenSource();

            try
            {
                //Debug("Setting timeout");
                //cts.CancelAfter(2000);
                //Debug("Attempting connection");
                //Task task = socket.ConnectAsync(hostname, "5104").AsTask(cts.Token);
                //task.Wait();
                //task.Run();
                //Debug("Connected");
                //IsConnected = true;




                Debug("Setting timeout");
                cts.CancelAfter(2000);
                Debug("Attempting connection");
                await socket.ConnectAsync(hostname, "5104").AsTask(cts.Token);
                Debug("Connected");
                IsConnected = true;
                
            }
            catch (TaskCanceledException ex)
            {
                Debug("Connection Failed - Device Not Found");
                IsConnected = false;
                ErrorBox.Text = "Connection Failed - Device Not Found";
            }        
        }

        private void WriteDeviceTitle()
        {
            Debug("Writing device title - " + App.settings.Values["devicename"].ToString() + " (" + App.settings.Values["deviceaddress"].ToString() + ")");
            DeviceTitle.Text = App.settings.Values["devicename"].ToString() + " (" + App.settings.Values["deviceaddress"].ToString() + ")";
        }

        private async void SendStudyListCommand()
        {
            Debug("Sending Study List Command");
            if (!IsConnected)
            {
                await ConnectToSelectedDevice();
                if (IsConnected)
                {
                    Reader = new DataReader(socket.InputStream);
                    Writer = new DataWriter(socket.OutputStream);

                    Writer.WriteString("STUDYLIST\n");
                    await Writer.StoreAsync();
                    Debug("STUDYLIST COMMAND");
                    Reader.InputStreamOptions = InputStreamOptions.Partial;


                    while (ShouldStudyListKeepLooping)
                    {
                        GetStudyListData();
                    }

                    if (ErrorBox.Text == String.Empty)
                    {
                        ShowStudyListData();
                    }
                }
                CloseConnection();
            }
        }

        private void CloseConnection()
        {
            Debug("Disposing of socket.");
            socket.Dispose();
            IsConnected = false;
            Debug("Not Connected");
        }

        private void GetStudyListData()
        {
            //Debug("Getting Study List Data");
            try
            {
                IAsyncOperation<uint> task = Reader.LoadAsync(1024);
                task.AsTask().Wait();
                counter = task.GetResults();
                Debug("Got StudyList Data - (" + counter + " bytes)");
                sb.Append(Reader.ReadString(counter));
            }
            catch (Exception ex)
            {
                ErrorBox.Text = ex.Message.ToString();
                ShouldStudyListKeepLooping = false;
            }
            

            if (sb.ToString().Contains("</STUDYLIST>"))
            {
                ShouldStudyListKeepLooping = false;
            }
        }

        private void ShowStudyListData()
        {
            string xml = sb.ToString().Replace("Connected", "");
            XElement xmlStudies = XElement.Parse(xml);
            List<Study> Studies = (from study in xmlStudies.Descendants("STUDY")
                                    select new Study
                                    {
                                        PatientName = study.Element("DCM_00100010").Value,
                                        PatientID = study.Element("DCM_00100020").Value,
                                        StudyInstanceUID = study.Element("DCM_0020000D").Value,
                                        TimeStamp = new DateTime(Int32.Parse(study.Element("DCM_00080020").Value.Substring(0, 4)), Int32.Parse(study.Element("DCM_00080020").Value.Substring(4, 2)), Int32.Parse(study.Element("DCM_00080020").Value.Substring(6, 2)), Int32.Parse(study.Element("DCM_00080030").Value.Substring(0, 2)), Int32.Parse(study.Element("DCM_00080030").Value.Substring(2, 2)), 0)
                                    }).ToList();
            StudyList.ItemsSource = Studies;
            WriteDeviceTitle();
        }

        private void StudyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            Study study = ((Study)StudyList.Items[StudyList.SelectedIndex]);
            SendAllImagesCommand(study);
        }

        private async void SendAllImagesCommand(Study study)
        {
            Debug("\nSending All Images Command");
            if (!IsConnected)
            {
                await ConnectToSelectedDevice();
                if (IsConnected)
                {
                    currentImages = new List<BitmapImage>();
                    if (!IsGettingImages)
                    {
                        IsGettingImages = true;
                        Reader = new DataReader(socket.InputStream);
                        Writer = new DataWriter(socket.OutputStream);

                        Writer.WriteString("ALLIMAGES " + study.StudyInstanceUID + "\n");
                        await Writer.StoreAsync();
                        Debug("ALLIMAGES " + study.StudyInstanceUID);
                        Reader.InputStreamOptions = InputStreamOptions.Partial;


                        while (ShouldAllImagesKeepLooping)
                        {
                            GetAllImagesHeader();
                            if (FileHeader.Length != 16)
                            {
                                ShouldAllImagesKeepLooping = false;
                                Debug("Image Header Invalid - " + FileHeader);
                                //return;
                            }
                            else
                            {
                                imagebytearray = new byte[0];
                                string imagename = FileHeader.Substring(0, 8);
                                currentbytes = UInt32.Parse(FileHeader.Substring(8, 8).ToString(), System.Globalization.NumberStyles.HexNumber);

                                if (imagename.Contains(".jpg"))
                                {
                                    while ((currentbytes > 0) && (ShouldAllImagesKeepLooping))
                                    {
                                        currentbytes = GetAllImagesData(currentbytes);
                                    }

                                    if (imagename.Contains(".mov"))
                                    {

                                    }
                                    else if (imagename.Contains(".jpg"))
                                    {
                                        ByteArrayToBitmapImage(imagebytearray);
                                        currentImages.Add(currentImage);
                                    }
                                }
                                else
                                {
                                    ShouldAllImagesKeepLooping = false;
                                }
                            }
                            
                        }

                        if (ErrorBox.Text == String.Empty)
                        {
                            ShowAllImagesData();
                            ShouldAllImagesKeepLooping = true;
                        }

                        IsGettingImages = false;
                    }
                }
            }
            CloseConnection();
        }

        private void ShowAllImagesData()
        {
            Debug("Showing All Images Data");
            ImagePanel.Children.Clear();
            foreach (BitmapImage i in currentImages)
            {
                Debug("Writing Image");
                Image j = new Image();
                ImageSource s = i;
                j.Source = s;
                ImagePanel.Children.Add(j);
            }
            
        }

        private void GetAllImagesHeader()
        {
            Debug("Getting Image Header");
            FileHeader = "";
            try
            {
                IAsyncOperation<uint> task = Reader.LoadAsync(16);
                task.AsTask().Wait();
                counter = task.GetResults();
                //counter = await Reader.LoadAsync(16);
                FileHeader = Reader.ReadString(counter);
                Debug("Got Image Header");
                if (FileHeader == "Connected")
                {
                    Debug("FileHeader == Connected");
                    Debug("Calling GetAllImagesHeader() Again");
                    GetAllImagesHeader();
                }
            }
            catch (Exception ex)
            {
                ErrorBox.Text = ex.Message.ToString();
                ShouldAllImagesKeepLooping = false;
                Debug("Failed to get image header");
            }
        }

        private uint GetAllImagesData(uint bytes)
        {
            uint bytecounter = 0;
            byte[] y = new byte[0];
            
            try
            {
                uint x = 16384;
                if (currentbytes < x) x = currentbytes;
                IAsyncOperation<uint> task = Reader.LoadAsync(x);
                task.AsTask().Wait();
                bytecounter = task.GetResults();
                y = new byte[bytecounter];
                Reader.ReadBytes(y);
                imagebytearray = MergeByteArrays(imagebytearray, y);
            }
            catch (Exception ex)
            {
                ErrorBox.Text = ex.Message.ToString();
                bytecounter = bytes;
                ShouldAllImagesKeepLooping = false;
            }
            uint f = (uint)y.Count();
            uint g = bytes - f; 
            return g;
        }

        private byte[] MergeByteArrays(byte[] a, byte[] b)
        {
            int newSize = a.Length + b.Length;
            var ms = new MemoryStream(new byte[newSize], 0, newSize, true);
            ms.Write(a, 0, a.Length);
            ms.Write(b, 0, b.Length);
            byte[] merged = ms.ToArray();
            return merged;
        }


        private async void ByteArrayToBitmapImage(byte[] byteArray)
        {

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(byteArray.AsBuffer());
            stream.Seek(0);

            currentImage.SetSource(stream);
        }

        private void WorklistButton_Click(object sender, RoutedEventArgs e)
        {
            WorklistFlyout.ShowAt(WorklistButton);
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectFlyout.ShowAt(ConnectButton);
        }

        private void SaveDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if ((DeviceNameBox.Text != "") && (DeviceAddressBox.Text != ""))
            {
                App.settings.Values["devicelist"] += DeviceNameBox.Text + "|" + DeviceAddressBox.Text + ";";
                LoadDeviceList();
            }
        }

        private void LoadDeviceList()
        {
            Debug("Loading Device List");
            List<Device> Devices = new List<Device>();
            Device defaultDevice = null;
            var list = (App.settings.Values["devicelist"]).ToString().Split(';');
            
            foreach (var l in list)
            {
                if (l.Contains("|"))
                {
                    var pair = l.Split('|');
                    Device d = new Device { Name = pair[0], IPAddress = pair[1] };
                    if ((d.IPAddress == App.settings.Values["deviceaddress"].ToString()) && (d.Name == App.settings.Values["devicename"].ToString()))
                    {
                        defaultDevice = d;
                    }
                    Devices.Add(d);
                }
            }

            DeviceList.ItemsSource = Devices;

            if (defaultDevice != null) DeviceList.SelectedItem = defaultDevice;
        }

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Device d = (Device)DeviceList.SelectedItem;
            App.settings.Values["devicename"] = d.Name;
            App.settings.Values["deviceaddress"] = d.IPAddress;
            App.settings.Values["devicepin"] = d.PIN;
            App.settings.Values["devicetoken"] = d.GUID;
            //ConnectToSelectedDevice(true);
            SendStudyListCommand();
        }

        private async void SaveWorklistButton_Click(object sender, RoutedEventArgs e)
        {
            bool ShouldSaveWorklist = true;
            
            if (FirstNameBox.Text == "")
            {
                FirstNameBox.Background = new SolidColorBrush(Colors.Red);
                ShouldSaveWorklist = false;
            }
            if (LastNameBox.Text == "")
            {
                LastNameBox.Background = new SolidColorBrush(Colors.Red);
                ShouldSaveWorklist = false;
            }
            if (IDBox.Text == "")
            {
                IDBox.Background = new SolidColorBrush(Colors.Red);
                ShouldSaveWorklist = false;
            }


            if (ShouldSaveWorklist)
            {
                await ConnectToSelectedDevice();
                //Debug("WORKLIST");
                string worklistXML = "WORKLIST";
                worklistXML += "<DCM_00100010>" + LastNameBox.Text + "^" + FirstNameBox.Text + "</DCM_00100010>";
                worklistXML += "<DCM_00100020>" + IDBox.Text + "</DCM_00100020>";
                worklistXML += "<DCM_00100030>" + DateBox.Date.ToString("yyyyMMdd", null as DateTimeFormatInfo) + "</DCM_00100030>";
                worklistXML += "<DCM_00100040>" + GetGenderCode() + "</DCM_00100040>\n";
                Debug(worklistXML);
                WorkListWriter = new DataWriter(socket.OutputStream);

                WorkListWriter.WriteString(worklistXML);
                Debug("Writing Worklist Command");
                await WorkListWriter.StoreAsync();
                Debug("Worklist completed.");
                CloseConnection();
            }


        }

        private void MaleButton_Click(object sender, RoutedEventArgs e)
        {
            SetGenderButtons(1);
        }

        private string GetGenderCode()
        {
            if (FemaleButton.Opacity == 1) return "F";
            else return "M";
        }

        private void SetGenderButtons(int p)
        {
            switch (p)
            {
                case 0:
                    FemaleButton.Opacity = 1;
                    MaleButton.Opacity = .4;
                    break;
                case 1:
                    FemaleButton.Opacity = .4;
                    MaleButton.Opacity = 1;
                    break;
            }
        }

        private void FemaleButton_Click(object sender, RoutedEventArgs e)
        {
            SetGenderButtons(0);
        }

        private void FirstNameBox_GotFocus(object sender, RoutedEventArgs e)
        {
            RemoveRed((TextBox)sender);
        }

        private void RemoveRed(TextBox sender)
        {
            sender.Background = new SolidColorBrush(Colors.White);
        }

        private void LastNameBox_GotFocus(object sender, RoutedEventArgs e)
        {
            RemoveRed((TextBox)sender);
        }

        private void IDBox_GotFocus(object sender, RoutedEventArgs e)
        {
            RemoveRed((TextBox)sender);
        }
    }
}
