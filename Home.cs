using AForge.Video.DirectShow;
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Accord.Video;
using Accord.Video.FFMPEG;
using System.Runtime.InteropServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace IP_Camera_App
{
    public partial class Home : Form
    {
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect,
            int nTopTect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
        );

        private MJPEGStream videoSource;
        private VideoFileWriter videoWriter;
        private bool isRecording = false;
        private string outputPath;
        private int recordingCount = 0;
        private readonly object lockObject = new object();

        public Home()
        {
            InitializeComponent();
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 25, 25));
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            string url = txtUrl.Text.Trim();

            // Check if the TextBox is empty
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Please enter the IP camera URL.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Add "http://" to the beginning if it's missing
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            // Validate the URL format (for IP address and optional port)
            string ipPattern = @"^(http:\/\/|https:\/\/)?(\d{1,3}\.){3}\d{1,3}(:\d+)?(\/.*)?$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(url, ipPattern))
            {
                MessageBox.Show("Please enter a valid IP camera URL (e.g., http://192.168.1.100:8080).", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Create the MJPEGStream object
                videoSource = new MJPEGStream(url);

                // Test the stream to ensure it provides frames
                bool streamAvailable = TestStream(videoSource);

                if (streamAvailable)
                {
                    // Subscribe to NewFrame event and start the stream
                    videoSource.NewFrame += new NewFrameEventHandler(video_NewFrame);
                    videoSource.Start();
                    MessageBox.Show("Video stream started successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("The provided URL is not streaming video. Please check the camera setup.", "Stream Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                // Handle errors when trying to start the stream
                MessageBox.Show($"Failed to start the video stream. Please check the IP address and ensure the camera is accessible.\n\nError: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        // Method to test the video stream
        private bool TestStream(MJPEGStream stream)
        {
            bool frameReceived = false;

            // Temporary event handler to capture a frame
            NewFrameEventHandler handler = (sender, eventArgs) =>
            {
                frameReceived = true;
                stream.SignalToStop(); // Stop the stream after receiving a frame
            };

            try
            {
                stream.NewFrame += handler;
                stream.Start();

                // Wait briefly to see if a frame is received
                DateTime timeout = DateTime.Now.AddSeconds(5);
                while (!frameReceived && DateTime.Now < timeout)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            finally
            {
                stream.SignalToStop();
                stream.WaitForStop();
                stream.NewFrame -= handler; // Remove the temporary handler
            }

            return frameReceived;
        }




        private void video_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            Bitmap bitmap = null;
            lock (lockObject)
            {
                bitmap = (Bitmap)eventArgs.Frame.Clone();
                if (isRecording && videoWriter != null)
                {
                    videoWriter.WriteVideoFrame(bitmap);
                }
            }

            UpdatePictureBox(bitmap);
        }
        private void UpdatePictureBox(Bitmap bitmap)
        {
            if (pictureBox.InvokeRequired)
            {
                pictureBox.Invoke(new Action<Bitmap>(UpdatePictureBox), bitmap);
            }
            else
            {
                // Dispose of the old image if it exists
                if (pictureBox.Image != null)
                {
                    pictureBox.Image.Dispose();
                }

                // Set the new image
                pictureBox.Image = bitmap;

            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            if (isRecording)
            {
                // Stop recording
                isRecording = false;

                lock (lockObject)
                {
                    if (videoWriter != null)
                    {
                        videoWriter.Close();
                        videoWriter = null;
                    }
                }

                // Stop the video source
                if (videoSource != null && videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource.WaitForStop();
                }

                // Now show the message and exit
                MessageBox.Show($"Recording stopped and saved to {outputPath}", "Recording Stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // Exit the application
            Application.Exit();
        }



        private void btnStartRecording_Click(object sender, EventArgs e)
        {
            if (videoSource == null || !videoSource.IsRunning)
            {
                MessageBox.Show("Please start the video stream first.");
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string sequenceNumber = (++recordingCount).ToString("D3");
            string filename = $"recording_{timestamp}_{sequenceNumber}.avi";

            string downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            outputPath = Path.Combine(downloadsFolder, filename);

            // Adjust the dimensions to be multiples of 2
            int width = pictureBox.Width - (pictureBox.Width % 2);
            int height = pictureBox.Height - (pictureBox.Height % 2);

            videoWriter = new VideoFileWriter();
            videoWriter.Open(outputPath, width, height, 25, VideoCodec.MPEG4, 1000000);
            MessageBox.Show("Recording has started.");
            isRecording = true;
        }


        private void btnStopRecording_Click(object sender, EventArgs e)
        {
            // Check if the video stream is active
            if (videoSource == null || !videoSource.IsRunning)
            {
                MessageBox.Show("Please start the video stream first.");
                return;
            }

            // Check if recording is active
            if (!isRecording)
            {
                MessageBox.Show("Recording is not in progress. Please start the recording first.");
                return;
            }

            // Stop recording
            isRecording = false;
            lock (lockObject)
            {
                videoWriter.Close();
            }
            videoWriter = null;
            MessageBox.Show($"Recording stopped and saved to {outputPath}");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop the video source if it's running
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.WaitForStop();
            }

            // Stop recording if it is in progress
            if (isRecording)
            {
                isRecording = false;
                lock (lockObject)
                {
                    videoWriter.Close();
                }
                videoWriter = null;
                MessageBox.Show($"Recording stopped and saved to {outputPath}");
            }

            base.OnFormClosing(e);
        }

    }
}
