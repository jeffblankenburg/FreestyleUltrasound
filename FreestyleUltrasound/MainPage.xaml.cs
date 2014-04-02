using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
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
        StreamSocket socket;
        DataReader reader;
        DataWriter writer;
        StringBuilder sb = new StringBuilder();
        List<BitmapImage> currentImages = new List<BitmapImage>();
        BitmapImage currentImage = new BitmapImage();
        uint currentbytes = 0;
        byte[] imagebytearray;
        uint counter = 0;
        bool ShouldStudyListKeepLooping = true;
        bool ShouldAllImagesKeepLooping = true;
        
        public MainPage()
        {
            this.InitializeComponent();

            //FreestyleFinder();
            Connect();
        }

        private void FreestyleFinder()
        {
            
        }

        private async void Connect()
        {
            try
            {
                socket = new StreamSocket();
                HostName hostname = new HostName("192.168.1.23");
                await socket.ConnectAsync(hostname, "5104");

                SendStudyListCommand();
            }
            catch (Exception ex)
            {
                ErrorBox.Text = SocketError.GetStatus(ex.HResult).ToString();
            }
                        
        }

        private async void SendStudyListCommand()
        {
            reader = new DataReader(socket.InputStream);
            reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            writer = new DataWriter(socket.OutputStream);

            writer.WriteString("STUDYLIST\n");
            await writer.StoreAsync();

            reader.InputStreamOptions = InputStreamOptions.Partial;


            while (ShouldStudyListKeepLooping)
            {
                GetStudyListData();
            }

            if (ErrorBox.Text == String.Empty)
            {
                ShowStudyListData();
            }
        }

        private void GetStudyListData()
        {
            try
            {
                IAsyncOperation<uint> task = reader.LoadAsync(1024);
                task.AsTask().Wait();
                counter = task.GetResults();
                sb.Append(reader.ReadString(counter));
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
        }

        private void StudyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            Study study = ((Study)StudyList.Items[StudyList.SelectedIndex]);
            SendAllImagesCommand(study);
        }

        private async void SendAllImagesCommand(Study study)
        {
            reader = new DataReader(socket.InputStream);
            writer = new DataWriter(socket.OutputStream);

            writer.WriteString("ALLIMAGES " + study.StudyInstanceUID + "\n");
            await writer.StoreAsync();

            reader.InputStreamOptions = InputStreamOptions.Partial;


            while (ShouldAllImagesKeepLooping)
            {
                string header = GetAllImagesHeader();
                imagebytearray = new byte[0];
                string imagename = header.Substring(0, 8);
                currentbytes = UInt32.Parse(header.Substring(8, 8).ToString(), System.Globalization.NumberStyles.HexNumber);

                while (currentbytes > 0)
                {
                    currentbytes = GetAllImagesData(currentbytes);
                }

                if (header.Contains(".mov"))
                {

                }
                else if (header.Contains(".jpg"))
                {
                    ByteArrayToBitmapImage(imagebytearray);
                    currentImages.Add(currentImage);
                }

                
            }

            if (ErrorBox.Text == String.Empty)
            {
                ShowAllImagesData();
            }
        }

        private void ShowAllImagesData()
        {
            ImagePanel.Children.Clear();
            foreach (BitmapImage i in currentImages)
            {
                Image j = new Image();
                ImageSource s = i;
                j.Source = s;
                ImagePanel.Children.Add(j);
            }
            
        }

        private string GetAllImagesHeader()
        {
            string header = "";
            
            try
            {
                IAsyncOperation<uint> task = reader.LoadAsync(16);
                task.AsTask().Wait();
                counter = task.GetResults();
                header = reader.ReadString(counter);
            }
            catch (Exception ex)
            {
                ErrorBox.Text = ex.Message.ToString();
                ShouldAllImagesKeepLooping = false;
            }

            return header;
        }

        private uint GetAllImagesData(uint bytes)
        {
            uint bytecounter = 0;
            byte[] y = new byte[0];
            
            try
            {
                uint x = 16384;
                if (currentbytes < x) x = currentbytes;
                IAsyncOperation<uint> task = reader.LoadAsync(x);
                task.AsTask().Wait();
                bytecounter = task.GetResults();
                y = new byte[bytecounter];
                reader.ReadBytes(y);
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
    }
}
