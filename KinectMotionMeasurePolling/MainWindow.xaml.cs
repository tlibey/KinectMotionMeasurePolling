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
using NAudio;
using NAudio.Wave;

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


namespace KinectMotionMeasurePolling
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
        int quadMarginXR = 20;
        int quadMarginYT = 20;
        int quadMarginYB = 20;//
        double CountThresh = 30.0; 
        int loThreshold = 1000; 
        int hiThreshold = 1548;
        int blocks2countset = 6; // max 6
        string detectionPattern = "regular"; //types "regular" || "lohilo"


        //logistics and file names
        static DateTime timeStart;
        static string CoreFileName = @"C:/MoveCalc/" + DateTime.Now.DayOfYear.ToString() + "_" + DateTime.Now.Hour.ToString() + "_" + DateTime.Now.Minute.ToString() + "_" + DateTime.Now.Second.ToString();
        string dfileName = CoreFileName + "/MovementValues.txt";
        string tfileName = CoreFileName + "/timeofMovementValues.txt";
        string vfileName = CoreFileName + "/Videos/";
        string sfileName = CoreFileName + "/settings.txt"; //this sessions end settings
        string recentSettingsFileName = @"C:/MoveCalc/recentSettings.txt";
        string eventTimesfileName = CoreFileName + "/timeofBehEvents.txt";
        string eventTimesTOfileName = CoreFileName + "/timeofBehEventsInTO.txt";
        string TITOfileName = CoreFileName + "/TITOtimeStamps.txt";

        //starter values
        bool timeOutPause = false;
        int timeOutFrameCount = 0; // initializes framecounter 
        int frameRate = 32;
        private int counter = 0; //successful event counter, displayed in top right corner and saved at the end of videos (event{0}.avi)
        int TOcounter = 0; //events occurring during TO

        //Things to play with
        int recordLength = 32 * 8;  //framerate*seconds to record (half before event and half after event)
        int frameAcceptance = 4; //x of n frames used in depth calculations
        int valDelayOpenNextFrame = 100; //100 default, change value??

        //time In parameters (only deliver treats during timeIn) 
        bool timeInCounter = true; //true if timeIn Initialized (ie start with TI: set to true)
        int timeInDuration = 120; //in seconds
        int timeOutDuration = 240;
        double lastTI2TO = 0;
        double lastTO2TI = 0;

        //email parameters
        bool emailON = true;
        bool firstemail = false;
        int emailUpdateFrequency = 30*60; // in seconds
        int emailCounter = 0; // how many emails have been sent
        double lastEmail = 0;


        //for audiofeedback
        bool toneOn = true;
        WaveOut waveOut = new WaveOut();
        SineWaveOscillator osc = new SineWaveOscillator(44100);

        //for low resource
        bool lowResource = true;
        int rectholder1 = 0;
        int rectholder2 = 0;
        int rectholder3 = 0;
        int rectholder4 = 0;
        int rectholder5 = 0;
        int rectholder6 = 0;

       

        #endregion Member Variables
        //NEED CREATE SETTINGS FILE!

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(CoreFileName);
            Directory.CreateDirectory(vfileName);
            if (File.Exists(dfileName))
            {
                File.Delete(dfileName);
            }
            if (File.Exists(tfileName))
            {
                File.Delete(tfileName);
            }
            if (File.Exists(vfileName))
            {
                File.Delete(vfileName);
            }
            if (File.Exists(eventTimesfileName))
            {
                File.Delete(eventTimesfileName);
            }
            if (File.Exists(sfileName))
            {
                File.Delete(sfileName);
            }
            if (File.Exists(TITOfileName))
            {
                File.Delete(TITOfileName);
            }
            if (File.Exists(eventTimesTOfileName))
            {
                File.Delete(eventTimesTOfileName);
            }

            //Load Settings File
            // lothresh, hithresh, yslider, xslider, threshslider
            if (File.Exists(recentSettingsFileName))
            {
                using (StreamReader sr = new StreamReader(recentSettingsFileName))
                {
                    CountThresh = Convert.ToDouble(sr.ReadLine());
                    loThreshold = Convert.ToInt16(sr.ReadLine());
                    hiThreshold = Convert.ToInt16(sr.ReadLine());
                    quadMarginXR = Convert.ToInt16(sr.ReadLine());
                    quadMarginXL = Convert.ToInt16(sr.ReadLine());
                    quadMarginYT = Convert.ToInt16(sr.ReadLine());
                    quadMarginYB = Convert.ToInt16(sr.ReadLine());
                    blocks2countset = Convert.ToInt16(sr.ReadLine());
                }
            }
            
                xQuadMarginSliderR.Value = quadMarginXR;
                xQuadMarginSliderL.Value = quadMarginXL;
                yQuadMarginSliderT.Value = quadMarginYT;
                yQuadMarginSliderB.Value = quadMarginYB;
                threshSlider.Value = CountThresh;
                loDepthSlider.Value = loThreshold;
                hiDepthSlider.Value = hiThreshold;
                EventNumNeeded_Slider.Value = blocks2countset;
            

            this._Worker = new BackgroundWorker();
            this._Worker.DoWork +=Worker_DoWork;
            this._Worker.RunWorkerAsync();

            //start tone
            osc.Amplitude = 8192;
            waveOut.Init(osc);
            waveOut.Play();


             this.Unloaded += (s, e) => 
             { 
             this.Kinect = null;
             this._Worker.CancelAsync();
             waveOut.Stop();
              };

        }
        #endregion Constructor

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
                // no kinect
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
                            if(currentFrame%frameAcceptance == 0) //set to only add every frameAcceptanceth'd frame
                                _videoArray.Add(frame.ToOpenCVImage<Rgb, Byte>());
                                     if (_videoArray.Count() > recordLength/frameAcceptance) // Frame limiter (ideally 4x where x is length of event)
                                         _videoArray.RemoveAt(0);
                              
                        }
                    }
                }
                catch(Exception ex)
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
                    {this._Kinect = null; }
                    if (value != null && value.Status == KinectStatus.Connected)
                    {this._Kinect = value;}
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
                    if (depth[i] < loThreshold || depth[i] > hiThreshold)
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

                //end test
                DepthImageModified.Source = BitmapSource.Create(depthFrame.Width, depthFrame.Height, 96, 96, PixelFormats.Bgr32, null, enhPixelData, depthFrame.Width * bytesPerPixel);
            }
            else
            {
                for (int i = 0, j = 0; i < pixelDataD.Length; i++, j += bytesPerPixel)
                {
                    depth[i] = pixelDataD[i] >> DepthImageFrame.PlayerIndexBitmaskWidth;
                    if (depth[i] < loThreshold || depth[i] > hiThreshold)
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

            if (timeOutPause) //placed here to ensure that half of the recorded time is after the event (time outPause defined as true when event happens
            {
                timeOutFrameCount++;
                if (timeOutFrameCount >= recordLength / 2)
                {
                    timeOutFrameCount = 0;
                    timeOutPause = false;
                    //implement option to not save videos during TO
                    SaveVideo();
                }
            }
            
        }

        private void calculateMovement(int[] depth, int fwidth, int fheight)
        {


            int[] iLeftPos = new int[16];
            int[] iTopPos = new int[16];
            int[] iRightPos = new int[16];
            int[] iDepthMin = new int[16];
            // int iDepthMax = 0;



            //TODO: determine if actually removing border issues

            int quadrantDiv = 4; // 4x4 division of pixel space
            // int quadMarginX = fwidth/4; //Defined twice - fix?
            //int quadMarginY = fheight/4;


            for (int quadY = 0; quadY < quadrantDiv; quadY++)
            {
                for (int quadX = 0; quadX < quadrantDiv; quadX++)
                {
                    //calculate leftmost and rightmost depths
                    //NEED TO FIX WITH NEW INDEPENDENT MARGIN VALUES!!!
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
                                //if (depth[depthIndex] > iDepthMax) not necessary?
                                // iDepthMax = depth[depthIndex];
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
                //  depthOld = depth;
                iLeftPosOld = iLeftPos;
                iTopPosOld = iTopPos;
                iRightPosOld = iRightPos;
                iDepthMinOld = iDepthMin;

                timeStart = DateTime.Now;
             }

            int iMovementValue = 0;
            int[] allMovementValue = new int[16];
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
                moveValue1.Text = string.Format("{0}", allMovementValue[0]);
                moveValue2.Text = string.Format("{0}", allMovementValue[1]);
                moveValue3.Text = string.Format("{0}", allMovementValue[2]);
                moveValue4.Text = string.Format("{0}", allMovementValue[3]);
                moveValue5.Text = string.Format("{0}", allMovementValue[4]);
                moveValue6.Text = string.Format("{0}", allMovementValue[5]);
                moveValue7.Text = string.Format("{0}", allMovementValue[6]);
                moveValue8.Text = string.Format("{0}", allMovementValue[7]);
                moveValue9.Text = string.Format("{0}", allMovementValue[8]);
                moveValue10.Text = string.Format("{0}", allMovementValue[9]);
                moveValue11.Text = string.Format("{0}", allMovementValue[10]);
                moveValue12.Text = string.Format("{0}", allMovementValue[11]);
                moveValue13.Text = string.Format("{0}", allMovementValue[12]);
                moveValue14.Text = string.Format("{0}", allMovementValue[13]);
                moveValue15.Text = string.Format("{0}", allMovementValue[14]);
                moveValue16.Text = string.Format("{0}", allMovementValue[15]);




                targetline.Height = Math.Abs(1 + (int)CountThresh);
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
                Rect25.Height = Math.Abs(1 + iMovementValue);
            }
            double targetlineholder = 1 + CountThresh;
            rectholder1 = rectholder2;
            rectholder2 = rectholder3;
            rectholder3 = rectholder4;
            rectholder4 = rectholder5;
            rectholder5 = rectholder6;
            rectholder6 = Math.Abs(1 + iMovementValue);

            //assigmnet
            depthOld = depth;
            updateToneFeedbackFrequency();
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

            //determine if should be in timein/timeout
            double currentTimeElapsed = Math.Floor(elapsed.TotalSeconds);

            if (elapsed.TotalSeconds > 1 )//&& lockoutVal != currentTimeElapsed)
            { //testing tolerances of 0.1s for large value calculations - might be better served with a full reset of currentTimeElapsed
                if (timeInCounter && (currentTimeElapsed >= lastTO2TI+timeInDuration))//(currentTimeElapsed % timeInDuration == 0) && timeInCounter) //TI=>TO
                {
                    timeInCounter = false;
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(TITOfileName, true))
                    {
                        file.Write(currentTimeElapsed - lastTO2TI - timeInDuration);

                        file.Write("__TI-TO: ");
                        file.WriteLine(elapsed.TotalMilliseconds);

                    }
                    lastTI2TO = currentTimeElapsed;

                  


                    //firstemail = true;   //to send email updates at the end of each timein, 
                }
                else if (!timeInCounter && (currentTimeElapsed >= lastTI2TO+timeOutDuration))//(currentTimeElapsed-lockoutVal==timeOutDuration)) //TO=>TI
                {
                    sendSerialTreat();
                    timeInCounter = true;
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(TITOfileName, true))
                    {
                        file.Write(currentTimeElapsed - lastTI2TO - timeOutDuration);

                        file.Write("__TO-TI: ");
                        file.WriteLine(elapsed.TotalMilliseconds);

                    }
                    lastTO2TI = currentTimeElapsed;

                }
                if (currentTimeElapsed >= lastEmail + emailUpdateFrequency)
                {
                    firstemail = true;
                    lastEmail = currentTimeElapsed;
                }
             

            }


            this.TimeElapsed.Dispatcher.BeginInvoke(new Action(() =>
           {
               TimeInDisp.Text = timeInCounter.ToString();
           }));
            if (!timeOutPause) //timeoutpause is lockout from event, time in counter is TI vs TO
            {
               
                bool tfcounter = true; //used in RectangleBehaviourCounter to make sure x bars are above line ie true*true = true, true*false = false;
                #region RectangleBehaviorCounter
                int blocks2count = blocks2countset;
                //compare Rect 25,multiply tfcounter by 1;
                if (detectionPattern == "regular")
                {
                    tfcounter = tfcounter && (rectholder6 > targetlineholder);
                    blocks2count--;
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder5 > targetlineholder);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder4 > targetlineholder);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder3 > targetlineholder);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder2 > targetlineholder);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder1 > targetlineholder);
                        blocks2count--;
                    }
                }
                else if (detectionPattern == "lohilo")
                {
                    double lo = targetlineholder / 2;
                    double hi = targetlineholder;
                    tfcounter = tfcounter && (rectholder6 < lo);
                    blocks2count--;
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder5 < lo);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder4 > hi);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder3 > hi);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder2 < lo);
                        blocks2count--;
                    }
                    if (blocks2count > 0)
                    {
                        tfcounter = tfcounter && (rectholder1 < lo);
                        blocks2count--;
                    }


                }
                

                #endregion

                //define event to count
                if (tfcounter && timeInCounter) //
                {
                    counter++;
                    this.TimeElapsed.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Counter.Text = string.Format("{0} Events", counter);
                    })); timeOutPause = true;
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(eventTimesfileName, true))
                    {
                        file.WriteLine(elapsed.TotalMilliseconds);

                    }

                    //DELIVER TREAT()!!!!
                    if (Environment.UserName == "fetzlab" && SerialPort.GetPortNames().Any(x => string.Compare(x,"COM4",true)==0))
                    {
                        sendSerialTreat();
                    }

                }
                else if (tfcounter && !timeInCounter)
                {
                    TOcounter++;
                    timeOutPause = true;
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(eventTimesTOfileName, true))
                    {
                        file.WriteLine(elapsed.TotalMilliseconds);

                    }
                }
            }

            if(!timeInCounter && firstemail && emailON)
            { sendEmailUpdate();
            firstemail = false;
            }


            using (System.IO.StreamWriter file = new System.IO.StreamWriter(dfileName, true))
            {
                file.WriteLine(iMovementValue);

            }
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(tfileName, true))
            {
                file.WriteLine(elapsed.TotalMilliseconds);

            }


        }


        
        private void SaveVideo()
        {

            string vEventfileName = vfileName + "event" + (counter + TOcounter).ToString() + ".avi"; //was eventCounter.tostring()
            using (VideoWriter vw = new VideoWriter(vEventfileName, 0, frameRate/frameAcceptance, 640, 480, true))
            {
                for (int i = 0; i < _videoArray.Count(); i++)
                {

                    vw.WriteFrame<Emgu.CV.Structure.Rgb, Byte>(_videoArray[i]);
                }
            }
            //eventCounter++;
        }

        private void sendSerialTreat()
        {
            char[] test = new char[1];
            test[0] = 'A';


            SerialPort port1 = new SerialPort("COM4", 9600);
            port1.Open();
            port1.Write(test, 0, 1);
            // user.Text = port1.ReadByte().ToString();

            port1.Close();

        }

        private void updateToneFeedbackFrequency()
        {
            if (toneOn)
            {
                if (timeInCounter)
                {
                    osc.Amplitude = 8192;
                    if (CountThresh - rectholder6 <= 0)
                    {
                        osc.Frequency = 3000;
                    }
                    else
                    {
                        //try to smooth?
                        //linear
                        //osc.Frequency = Rect25.Height / (int)CountThresh * 3000 + 100;

                        //4 lines trailing
                        // osc.Frequency  = ((Rect25.Height / (int)CountThresh) + (Rect24.Height / (int)CountThresh) + 
                        //                  (Rect23.Height / (int)CountThresh) + (Rect22.Height / (int)CountThresh))*3000/4 + 100;

                        //parabolic
                        // osc.Frequency = Rect25.Height * Rect25.Height/ (int)CountThresh * 50 + 37;

                        //one trailing multi weights
                        
                        osc.Frequency = (rectholder6 / CountThresh * 1500 + rectholder5/ CountThresh * 1000 + 37) ;
                    }
                }
                if (!timeInCounter)
                {
                    osc.Frequency = 37;
                    osc.Amplitude = 0;
                }
            }
        }

        private void sendEmailUpdate()
        {
            //send Counter, timeElapsed, video?
            try
            {
                emailCounter++;
                System.Net.NetworkCredential cred = new System.Net.NetworkCredential("tylerplab@gmail.com", "*venta*venta");
                System.Net.Mail.MailMessage message = new System.Net.Mail.MailMessage();
                message.To.Add("tylerplab@gmail.com");
                message.To.Add("tlibey1@gmail.com");
                message.To.Add("zsr@uw.edu");
                message.From = new System.Net.Mail.MailAddress("tylerplab@gmail.com");
                message.Subject = "Update" + DateTime.Today.Date + emailCounter.ToString();
                this.TimeElapsed.Dispatcher.BeginInvoke(new Action(() =>
                {
                    message.Body = "Number of Events Completed: " + counter.ToString() + "\n" +
                                                   "Time Elapsed: " + TimeElapsed.Text + "\n" +
                                                   "Events in TO" + TOcounter.ToString() + "\n";
                }));
                



                System.Net.Mail.SmtpClient client = new System.Net.Mail.SmtpClient("smtp.gmail.com");
                client.Credentials = cred;
                client.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
                client.EnableSsl = true;
                client.Port = 587;
                client.Send(message);

            }
            catch
            {
                //no email
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

        private void threshSlider_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            CountThresh = (int)threshSlider.Value;
            CountThreshDisp.Text = CountThresh.ToString();
        }
        private void loDepthSlider_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            loThreshold = (int)loDepthSlider.Value;
            loDepthDisp.Text = string.Format("{0} mm", loThreshold);
        }
        private void hiDepthSlider_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            hiThreshold = (int)hiDepthSlider.Value;
            hiDepthDisp.Text = string.Format("{0} mm", hiThreshold);
        }
        private void EventNumNeeded_ValChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            blocks2countset = (int)EventNumNeeded_Slider.Value;
            EventNumVal.Text = string.Format("{0}", blocks2countset);

        }


        public void SaveSettingsButton(object sender, EventArgs a)
        {
            if (File.Exists(sfileName))
            {
                File.Delete(sfileName);
            }
            if (File.Exists(recentSettingsFileName))
            {
                File.Delete(recentSettingsFileName);
            }

            using (System.IO.StreamWriter file = new System.IO.StreamWriter(recentSettingsFileName, true))
            {
                file.WriteLine(CountThresh);
                file.WriteLine(loThreshold);
                file.WriteLine(hiThreshold);
                file.WriteLine(quadMarginXR);
                file.WriteLine(quadMarginXL);
                file.WriteLine(quadMarginYT);
                file.WriteLine(quadMarginYB);
                file.WriteLine(blocks2countset);
            }

            using (System.IO.StreamWriter file = new System.IO.StreamWriter(sfileName, true))
            {
                file.WriteLine(CountThresh);
                file.WriteLine(loThreshold);
                file.WriteLine(hiThreshold);
                file.WriteLine(quadMarginXR);
                file.WriteLine(quadMarginXL);
                file.WriteLine(quadMarginYT);
                file.WriteLine(quadMarginYB);
                file.WriteLine(blocks2countset);

            }
        }

        #endregion Properties

        private void FreeTreatFeederTest(object sender, EventArgs a)
        {
            char[] test = new char[1];
            test[0] = 'A';


            SerialPort port1 = new SerialPort("COM4", 9600);
            port1.Open();
            port1.Write(test, 0, 1);
            // user.Text = port1.ReadByte().ToString();

            port1.Close();
        }



        
           
    }
}
