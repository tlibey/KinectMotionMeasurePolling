using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Drawing;
using System.IO.Ports;
using Microsoft.Kinect;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.UI;
using Emgu.CV.Structure;
using System.IO;
using ImageManipulationExtensionMethods;

/*TODO: 
 *  deploy?
 *  change video saving, files too large and blue... huge disk writing slowdown
 * 
 * */

//Program Summary:
/*
 * Start kinect streams (polling). Take in visual/color data and depth data frames.
 * save color frames in Xs long arrays (where X is set based on the time surrounding a desired event), ie if want -2->+2 around event, X= 4
 * use depth data to determine if an event happened (calculates leftmost, rightmost, upmost, and minimum most, and finds differnce from last frame)
 * if this calculation is above a set threshold for a set number of frames then an event is triggered. This causes a feeder to go, a counter to increment and the video to save
 * it also causes a brief lockout period = X/2; 
 * other features:
 * at specified times an email will be send to tylerplab@gmail.com with varying details about the current status of the experiment
 * Time In and Time out values can be specified where during Time-out no detected events elicit the above actions
 * Audio feedback is provided in the form of tones based off of the depth calculation algorithms (this is also off during Time out)
 * 
 * Sliders are implemented to control multiple aspects of the project
 * - x,y,z margins such that only the center region is included in the depth calculation
 * - events to count (how many frames need to be above threshold)
 * - target threshold
 * 
 * Finally, all slider values can be saved with the onscreen button, which allows for the low resource version of the program to operate
 * 
 * 
 * 
 * 
 * 
 */


namespace KinectBehaviorMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Member Variables
        private KinectSensor _Kinect;
        private WriteableBitmap _ColorImageBitmap;
        private Int32Rect _ColorImageBitmapRect;
        private int _ColorImageStride;
        private WriteableBitmap _DepthImageBitmap;
        private Int32Rect _DepthImageBitmapRect;
        private int _DepthImageStride;
        private byte[] _ColorImagePixelData;
        private short[] _DepthImagePixelData;
        private BackgroundWorker _Worker;

        List<Image<Rgb, Byte>> _videoArray = new List<Image<Rgb, Byte>>(); //video file initializer

        int currentFrame = 0;
        static int[] depthOld = { 0 };
        static int[] iLeftPosOld = null;
        static int[] iTopPosOld = null;
        static int[] iRightPosOld = null;
        // static int iDepthMaxOld;
        static int[] iDepthMinOld = null;

        // initial values defined by slider widths
        int quadMarginXL = 20;
        int quadMarginXR = 40;
        int quadMarginYT = 20;
        int quadMarginYB = 40;//
        int loDepthThreshold = 1000;
        int hiDepthThreshold = 1548;


        //logistics and file names
        static DateTime timeStart;
        KinectBehavior_FileHandler fileHandler = new KinectBehavior_FileHandler();
        KinectBehavior_EmailHandler emailHandler;// = new KinectBehavior_EmailHandler(fileHandler);
        KinectBehavior_PortHandler portHandler;

        //starter values
        bool timeOutPause = false;
        int timeOutFrameCount = 0; // initializes framecounter 
        int frameRate = 32;
        private int counter = 0; //successful event counter, displayed in top right corner and saved at the end of videos (event{0}.avi)
        int TOcounter = 0; //events occurring during TO

        //Things to play with
        int videoRecordLength = 32 * 8;  //framerate*seconds to record (half before event and half after event)
        int frameAcceptance = 4; //x of n frames used in depth calculations
        int valDelayOpenNextFrame = 100; //100 default, change value??
        bool savingVideo = false;
      
        //for low resource
        bool lowResource = false;
 
        #endregion Member Variables

        public MainWindow() //main window constructor
        {
            //Start time here since its the first call
            timeStart = DateTime.Now;
            InitializeComponent();
            emailHandler = new KinectBehavior_EmailHandler(fileHandler);
            portHandler = new KinectBehavior_PortHandler(fileHandler);
            int[] loadedSettings = fileHandler.InitializeFileStructure();
            if (loadedSettings != null)
            {
                 quadMarginXL = loadedSettings[0];
                 quadMarginXR = loadedSettings[1];
                 quadMarginYT = loadedSettings[2];
                 quadMarginYB = loadedSettings[3];
                 loDepthThreshold = loadedSettings[4];
                 hiDepthThreshold = loadedSettings[5];
                 xQuadMarginSliderR.Value = quadMarginXR;
                 xQuadMarginSliderL.Value = quadMarginXL;
                 yQuadMarginSliderT.Value = quadMarginYT;
                 yQuadMarginSliderB.Value = quadMarginYB;
                 loDepthSlider.Value = loDepthThreshold;
                 hiDepthSlider.Value = hiDepthThreshold;
            }
            Console.WriteLine("Initialized");

            this._Worker = new BackgroundWorker();
            this._Worker.DoWork += Worker_DoWork;
            this._Worker.RunWorkerAsync();
            this.Unloaded += (s, e) =>
            {
                this.Kinect = null;
                this._Worker.CancelAsync();
            };

        }

        #region Methods

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            if (worker != null)
            {
                while (!worker.CancellationPending)
                {
                    DiscoverKinectSensor();
                    PollColorImageStream();
                    PollDepthImageStream();
                    savingVideo = portHandler.checkSerialInput(DateTime.Now.Subtract(timeStart).TotalSeconds); //check portData to see if arduino has triggered an event

                }
            }
        }
        private void DiscoverKinectSensor()
        {
            if (this._Kinect != null && this._Kinect.Status != KinectStatus.Connected)
            {
                this._Kinect = null;
            }
            if (this._Kinect == null)
            {
                this.Kinect = KinectSensor.KinectSensors.FirstOrDefault(x => x.Status == KinectStatus.Connected);

                if (this._Kinect != null)
                {
                    this._Kinect.ColorStream.Enable();
                    this._Kinect.Start();
                    ColorImageStream colorStream = this._Kinect.ColorStream;
                    DepthImageStream depthStream = this._Kinect.DepthStream;
                    this._Kinect.DepthStream.Enable();

                    if (!lowResource)
                    {
                        this.ColorImageElement.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            this._ColorImageBitmap = new WriteableBitmap(colorStream.FrameWidth, colorStream.FrameHeight, 96, 96, PixelFormats.Bgr32, null);
                            this._ColorImageBitmapRect = new Int32Rect(0, 0, colorStream.FrameWidth, colorStream.FrameHeight);
                            this._ColorImageStride = colorStream.FrameWidth * colorStream.FrameBytesPerPixel;
                            ColorImageElement.Source = this._ColorImageBitmap;
                            this._ColorImagePixelData = new byte[colorStream.FramePixelDataLength];
                        }));

                        this.DepthImageModified.Dispatcher.BeginInvoke(new Action(() =>
                        {

                            this._DepthImageBitmap = new WriteableBitmap(depthStream.FrameWidth, depthStream.FrameHeight, 96, 96, PixelFormats.Gray16, null);
                            this._DepthImageBitmapRect = new Int32Rect(0, 0, depthStream.FrameWidth, depthStream.FrameHeight);
                            this._DepthImageStride = depthStream.FrameWidth * depthStream.FrameBytesPerPixel;
                            this._DepthImagePixelData = new short[depthStream.FramePixelDataLength];
                        }));
                    }
                    else
                    {
                        this._ColorImagePixelData = new byte[colorStream.FramePixelDataLength];
                        this._DepthImagePixelData = new short[depthStream.FramePixelDataLength];

                    }
                }
            }
        }

        private void PollColorImageStream()
        {
            if (this._Kinect == null)
            {
                // Console.WriteLine("no kinect");
                               
            }

            else
            {
                try
                {
                    using (ColorImageFrame frame = this._Kinect.ColorStream.OpenNextFrame(valDelayOpenNextFrame))
                    {
                        if (frame != null)
                        {
                            frame.CopyPixelDataTo(this._ColorImagePixelData);
                            if (!lowResource)
                            {
                                this.ColorImageElement.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    this._ColorImageBitmap.WritePixels(this._ColorImageBitmapRect, this._ColorImagePixelData, this._ColorImageStride, 0);

                                }));
                            }
                            //add video images to array such that SaveVideo() can record video through opencv
                            if (currentFrame % frameAcceptance == 0) //set to only add every frameAcceptanceth'd frame
                                _videoArray.Add(frame.ToOpenCVImage<Rgb, Byte>());
                            if (_videoArray.Count() > videoRecordLength / frameAcceptance) // Frame limiter (ideally 4x where x is length of event)
                                _videoArray.RemoveAt(0);

                        }
                    }
                }
                catch (Exception ex)
                {
                    //report error?
                }
            }
        }
        private void PollDepthImageStream()
        {
            if (this._Kinect == null)
            {
                // no kinect
            }

            else
            {
                try
                {
                    using (DepthImageFrame frame = this._Kinect.DepthStream.OpenNextFrame(100))
                    {
                        if (frame != null)
                        {
                            frame.CopyPixelDataTo(this._DepthImagePixelData);

                            if (!lowResource)
                            {
                                this.DepthImageModified.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    this._DepthImageBitmap.WritePixels(this._DepthImageBitmapRect, this._DepthImagePixelData, this._DepthImageStride, 0);
                                    ModifyDepthImage(frame, _DepthImagePixelData);
                                }));
                            }
                            else
                            {
                                ModifyDepthImage(frame, _DepthImagePixelData);

                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                }
            }
        }

        #endregion Methods

        #region Properties
        public KinectSensor Kinect
        {
            get { return this._Kinect; }
            set
            {
                if (this._Kinect != value)
                {
                    if (this._Kinect != null)
                    { this._Kinect = null; }
                    if (value != null && value.Status == KinectStatus.Connected)
                    { this._Kinect = value; }
                }
            }
        }

        private void ModifyDepthImage(DepthImageFrame depthFrame, short[] pixelDataD)
        {

            int bytesPerPixel = 4;
            int[] depth = new int[depthFrame.Width * depthFrame.Height * bytesPerPixel];
            if (!lowResource)
            {
                int gray;
                byte[] enhPixelData = new byte[depthFrame.Width * depthFrame.Height * bytesPerPixel];
                for (int i = 0, j = 0; i < pixelDataD.Length; i++, j += bytesPerPixel)
                {
                    depth[i] = pixelDataD[i] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                    if (depth[i] < loDepthThreshold || depth[i] > hiDepthThreshold)
                    {
                        gray = 0xFF;
                        depth[i] = 0;
                    }
                    else
                    {
                        gray = (255 * depth[i] / 0xFFF);
                    }
                    enhPixelData[j] = (byte)gray;
                    enhPixelData[j + 1] = (byte)gray;
                    enhPixelData[j + 2] = (byte)gray;
                }


                // draw margins
                for (int iiy = 0; iiy < depthFrame.Height; iiy++)
                    for (int iix = 0; iix < depthFrame.Width; iix++)
                    {
                        if (iix == quadMarginXR || iiy == quadMarginYB)
                        {

                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel] = 0;
                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel + 1] = 0;
                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel + 2] = 255;

                        }
                        if (iix == quadMarginXL || iiy == quadMarginYT)
                        {

                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel] = 255;
                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel + 1] = 0;
                            enhPixelData[(iix + iiy * depthFrame.Width) * bytesPerPixel + 2] = 255;

                        }
                    }

                DepthImageModified.Source = BitmapSource.Create(depthFrame.Width, depthFrame.Height, 96, 96, PixelFormats.Bgr32, null, enhPixelData, depthFrame.Width * bytesPerPixel);
            }
            else
            {
                for (int i = 0, j = 0; i < pixelDataD.Length; i++, j += bytesPerPixel)
                {
                    depth[i] = pixelDataD[i] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                    if (depth[i] < loDepthThreshold || depth[i] > hiDepthThreshold)
                    {
                        depth[i] = 0;
                    }

                }
            }

            if (currentFrame % frameAcceptance == 0)
            {
                calculateMovement(depth, depthFrame.Width, depthFrame.Height);
                currentFrame = 0;
            }
            currentFrame++;

            if (savingVideo) //placed here to ensure that half of the recorded time is after the event (time outPause defined as true when event happens
            {
                timeOutFrameCount++;
                if (timeOutFrameCount >= videoRecordLength / 2)
                {
                    timeOutFrameCount = 0;
                    savingVideo = false;
                    //implement option to not save videos during TO
                    SaveVideo();
                }
            }

        }

        private void calculateMovement(int[] depth, int fwidth, int fheight)
        {

            int quadrantDiv = 20;
            int numberofQuadrants = quadrantDiv * quadrantDiv;
            int[] iLeftPos = new int[numberofQuadrants];
            int[] iTopPos = new int[numberofQuadrants];
            int[] iRightPos = new int[numberofQuadrants];
            int[] iDepthMin = new int[numberofQuadrants];
            //Add depthMax value?
            int[] allMovementValue = new int[numberofQuadrants];

            for (int quadY = 0; quadY < quadrantDiv; quadY++)
            {
                for (int quadX = 0; quadX < quadrantDiv; quadX++)
                {
                    //calculate leftmost and rightmost depths
                    for (int iiy = (quadY * (quadMarginYB - quadMarginYT) / quadrantDiv + quadMarginYT); iiy < (quadY * (quadMarginYB - quadMarginYT) / quadrantDiv + (quadMarginYB - quadMarginYT) / quadrantDiv + quadMarginYT); iiy++)
                    {
                        for (int iii = (quadX * (quadMarginXR - quadMarginXL) / quadrantDiv + quadMarginXL); iii < (quadX * (quadMarginXR - quadMarginXL) / quadrantDiv + (quadMarginXR - quadMarginXL) / quadrantDiv + quadMarginXL); iii++)
                        {
                            int depthIndex = iii + iiy * fwidth;
                            int quadDex = quadX + quadY * quadrantDiv;
                            if (depth[depthIndex] != 0)
                            {

                                if (iii > iLeftPos[quadDex])
                                    iLeftPos[quadDex] = iii;
                                if (iTopPos[quadDex] == 0)
                                    iTopPos[quadDex] = iiy;
                                if (depth[depthIndex] < iDepthMin[quadDex])
                                    iDepthMin[quadDex] = depth[depthIndex];
                              
                                break;
                            }
                        }
                        //calculate rightmost points
                        for (int iii = (quadX * (quadMarginXR - quadMarginXL) / quadrantDiv + (quadMarginXR - quadMarginXL) / quadrantDiv + quadMarginXL); iii > (quadX * (quadMarginXR - quadMarginXL) / quadrantDiv + quadMarginXL); iii--)
                        {
                            if (depth[iii + iiy * fwidth] != 0)
                            {

                                if (iii > iRightPos[quadX + quadY * quadrantDiv])
                                    iRightPos[quadX + quadY * quadrantDiv] = iii;
                                break;
                            }
                        }
                    }
                }
            }
            if (iLeftPosOld == null) // initializer when no previous depth value exists
            {
                iLeftPosOld = iLeftPos;
                iTopPosOld = iTopPos;
                iRightPosOld = iRightPos;
                iDepthMinOld = iDepthMin;

                
            }

            int iMovementValue = 0;
            for (int quadY = 0; quadY < quadrantDiv; quadY++)
            {
                for (int quadX = 0; quadX < quadrantDiv; quadX++)
                {
                    int quadDex = quadX + quadY * quadrantDiv;
                    int leftDiff = 0;
                    int rightDiff = 0;
                    int topDiff = 0;
                    int depthDiff = 0;

                    //dont want to subtract zero values from old or new
                    if (iLeftPos[quadDex] != 0 && iLeftPosOld[quadDex] != 0)
                        leftDiff = Math.Abs(iLeftPos[quadDex] - iLeftPosOld[quadDex]);

                    if (iRightPos[quadDex] != 0 && iRightPosOld[quadDex] != 0)
                        rightDiff = Math.Abs(iRightPos[quadDex] - iRightPosOld[quadDex]);

                    if (iTopPos[quadDex] != 0 && iTopPosOld[quadDex] != 0)
                        topDiff = Math.Abs(iTopPos[quadDex] - iTopPosOld[quadDex]);

                    if (iDepthMin[quadDex] != 0 && iDepthMinOld[quadDex] != 0)
                        depthDiff = Math.Abs(iDepthMin[quadDex] - iDepthMinOld[quadDex]);
                    // end section

                    if (leftDiff < 100 && rightDiff < 100 && topDiff < 100 && depthDiff < 100)
                        allMovementValue[quadDex] = leftDiff + rightDiff + topDiff + depthDiff;

                    iMovementValue += allMovementValue[quadDex]; //calculate final movement value
                }
            }

            this.DepthDiff.Dispatcher.BeginInvoke(new Action(() =>
            {
                DepthDiff.Text = string.Format("{0} mm", iMovementValue);
            }));

            if (!lowResource)
            {
                updateRealTimeDataTrace(iMovementValue);
            }
 
            //assign old values for next frame comparison
            depthOld = depth;
            //leftright reassignments
            iLeftPosOld = iLeftPos;
            iRightPosOld = iRightPos;
            iTopPosOld = iTopPos;
            iDepthMinOld = iDepthMin;



            TimeSpan elapsed = DateTime.Now.Subtract(timeStart);
            this.TimeElapsed.Dispatcher.BeginInvoke(new Action(() =>
            {
                TimeElapsed.Text = string.Format("{0} s", elapsed.TotalSeconds);
            }));

            double currentTimeElapsed = Math.Floor(elapsed.TotalSeconds);
            emailHandler.CheckEmailSend(currentTimeElapsed,counter);
            fileHandler.SaveMovementData(elapsed.TotalMilliseconds, allMovementValue);



            /* Currently not being used; This is where to implement dual conditioning
            if (!savingVideo) //timeoutpause is lockout from event for video saving, 
            {

                bool KinectFeederTrigger = false; 
 
                //define event to count
                if (KinectFeederTrigger) //
                {
                    counter++;
                    fileHandler.SaveEventData(elapsed.TotalMilliseconds, "TIEvent");

                    this.TimeElapsed.Dispatcher.BeginInvoke(new Action(() =>
                    {Counter.Text = string.Format("{0} Events", counter);}));
                    savingVideo = true;
                    portHandler.sendSerialTreat();
                    
                }
            }*/
        }

        private void updateRealTimeDataTrace(int newValue)
        {
            Rect1.Height = Rect2.Height;
            Rect2.Height = Rect3.Height;
            Rect3.Height = Rect4.Height;
            Rect4.Height = Rect5.Height;
            Rect5.Height = Rect6.Height;
            Rect6.Height = Rect7.Height;
            Rect7.Height = Rect8.Height;
            Rect8.Height = Rect9.Height;
            Rect9.Height = Rect10.Height;
            Rect10.Height = Rect11.Height;
            Rect11.Height = Rect12.Height;
            Rect12.Height = Rect13.Height;
            Rect13.Height = Rect14.Height;
            Rect14.Height = Rect15.Height;
            Rect15.Height = Rect16.Height;
            Rect16.Height = Rect17.Height;
            Rect17.Height = Rect18.Height;
            Rect18.Height = Rect19.Height;
            Rect19.Height = Rect20.Height;
            Rect20.Height = Rect21.Height;
            Rect21.Height = Rect22.Height;
            Rect22.Height = Rect23.Height;
            Rect23.Height = Rect24.Height;
            Rect24.Height = Rect25.Height;

            int newrectHeight = newValue / 10;
            Rect25.Height = Math.Abs(1 + newValue);


        }

        private void SaveVideo()
        {

            string vEventfileName = fileHandler.getVideoFileName() + "event" + (counter + TOcounter).ToString() + ".avi"; //
            using (VideoWriter vw = new VideoWriter(vEventfileName, 0, frameRate / frameAcceptance, 640, 480, true))
            {
                for (int i = 0; i < _videoArray.Count(); i++)
                {
                    vw.WriteFrame<Emgu.CV.Structure.Rgb, Byte>(_videoArray[i]);
                }
            }
        }

       

       

       

        //FUNCTIONS not USED IN LOW RESOURCE VERSION

        //slider controls
        private void xMarginRight_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            quadMarginXR = (int)xQuadMarginSliderR.Value;
            xMarginRightDisp.Text = quadMarginXR.ToString();
        }
        private void xMarginLeft_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            quadMarginXL = (int)xQuadMarginSliderL.Value;
            xMarginLeftDisp.Text = quadMarginXL.ToString();
        }
        private void yMarginTop_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            quadMarginYT = (int)yQuadMarginSliderT.Value;
            yMarginTopDisp.Text = quadMarginYT.ToString();
        }
        private void yMarginBottom_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            quadMarginYB = (int)yQuadMarginSliderB.Value;
            yMarginBottomDisp.Text = quadMarginYB.ToString();
        }

       
        private void loDepthSlider_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            loDepthThreshold = (int)loDepthSlider.Value;
            loDepthDisp.Text = string.Format("{0} mm", loDepthThreshold);
        }
        private void hiDepthSlider_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            hiDepthThreshold = (int)hiDepthSlider.Value;
            hiDepthDisp.Text = string.Format("{0} mm", hiDepthThreshold);
        }
      

        public void SaveSettingsButton(object sender, EventArgs a)
        {
            int[] settings = new int[]{quadMarginXL, quadMarginXR, quadMarginYT, quadMarginYB, loDepthThreshold, hiDepthThreshold};
            fileHandler.saveSettings(settings);
        }


        #endregion Properties

        private void FreeTreatFeederTest(object sender, EventArgs a)
        {
            portHandler.sendSerialTreat();

        }





    }
}
