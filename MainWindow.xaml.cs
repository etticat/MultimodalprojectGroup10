﻿
namespace ShapeGame
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Media;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Threading;
    using Microsoft.Kinect;
    using Microsoft.Kinect.Toolkit;
    using Microsoft.Maps.MapControl.WPF;
    using Microsoft.Samples.Kinect.WpfViewers;
    using ShapeGame.Speech;
    using ShapeGame.Utils;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {


        public static class NativeMethods
        {

            public static Microsoft.Maps.MapControl.WPF.Location mapLocBeforeClick;
            public const int InputMouse = 0;

            public const int MouseEventMove = 0x0001;
            public const int MouseEventLeftDown = 0x0002;
            public const int MouseEventLeftUp = 0x0004;
            public const int MouseEventRightDown = 0x08;
            public const int MouseEventRightUp = 0x10;
            public const int MouseEventAbsolute = 0x8000;

            public static bool lastLeftDown;

            [DllImport("user32.dll")]
            public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

            public static void SendMouseInput(int positionX, int positionY, int maxX, int maxY, bool leftDown, Map thisMap)
            {
                if (positionX > int.MaxValue)
                    throw new ArgumentOutOfRangeException("positionX");
                if (positionY > int.MaxValue)
                    throw new ArgumentOutOfRangeException("positionY");

                // mouse cursor position relative to the screen
                int mouseCursorX = (positionX * 65535) / maxX;
                int mouseCursorY = (positionY * 65535) / maxY;


                // determine if we need to send a mouse down or mouse up event
                if (!lastLeftDown && leftDown)
                {
                    System.Windows.Input.MouseButtonEventArgs eventArgs = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
                    // triggers the left click event on the map
                    thisMap.RaiseEvent(eventArgs);
                    lastLeftDown = leftDown;

                    return;
                }
                else if (lastLeftDown && !leftDown)
                {
                    mouse_event(MouseEventLeftUp, mouseCursorX, mouseCursorY, 0, 0);

                    lastLeftDown = leftDown;
                    return;
                }

                mouse_event(MouseEventAbsolute | MouseEventMove, mouseCursorX, mouseCursorY, 0, 0);

            }

            // resets the mouse event to stop clicking
            public static void resetMouseEvents()
            {
                mouse_event(MouseEventLeftUp, 0, 0, 0, 0);
            }
        }

        public static readonly DependencyProperty KinectSensorManagerProperty =
            DependencyProperty.Register(
                "KinectSensorManager",
                typeof(KinectSensorManager),
                typeof(MainWindow),
                new PropertyMetadata(null));

        #region Private State
        private const int TimerResolution = 2;  // ms
        private const int NumIntraFrames = 3;
        private const double MaxFramerate = 70;
        private const double MinFramerate = 15;
        private const double MinShapeSize = 12;
        private const double MaxShapeSize = 90;

        private readonly Dictionary<int, Player> players = new Dictionary<int, Player>();
        private readonly KinectSensorChooser sensorChooser = new KinectSensorChooser();
        
        private DateTime lastFrameDrawn = DateTime.MinValue;
        private DateTime predNextFrame = DateTime.MinValue;
        private double actualFrameTime;

        private Skeleton[] skeletonData;

        // Player(s) placement in scene (z collapsed):
        private Rect playerBounds;
        private Rect screenRect;

        private double targetFramerate = MaxFramerate;
        private int frameCount;
        private bool runningGameThread;

        private SpeechRecognizer mySpeechRecognizer;
        private int cursorX = 0;
        private int cursorY = 0;
        private float defaultZoom = 10;
        private float currentSetZoom = 10;
        private double latitude;
        private double longitude;
        private Player.Playermode lastPlayerMode;

        private enum TravelMode
        {
            Car, Walk, Bike, PublicTransport
        }
        private TravelMode transportMode = TravelMode.Car;



        #endregion Private State

        #region ctor + Window Events

        public MainWindow()
        {
            this.KinectSensorManager = new KinectSensorManager();
            this.KinectSensorManager.KinectSensorChanged += this.KinectSensorChanged;
            this.DataContext = this.KinectSensorManager;

            InitializeComponent();

            sensorChooser.Start();

            // Bind the KinectSensor from the sensorChooser to the KinectSensor on the KinectSensorManager
            var kinectSensorBinding = new Binding("Kinect") { Source = this.sensorChooser };
            BindingOperations.SetBinding(this.KinectSensorManager, KinectSensorManager.KinectSensorProperty, kinectSensorBinding);

            this.RestoreWindowState();
        }

        public void Navigate(Microsoft.Maps.MapControl.WPF.Location start, Microsoft.Maps.MapControl.WPF.Location end)
        {
            // Move camera to university
            this.myMap.Dispatcher.Invoke(new Action(() => { this.myMap.SetView(
                start, defaultZoom);
            }));

            BingServices.RouteResult route = CalculateRoute(start, end);
            ShowRouteOnMap(route);
        }

        public KinectSensorManager KinectSensorManager
        {
            get { return (KinectSensorManager)GetValue(KinectSensorManagerProperty); }
            set { SetValue(KinectSensorManagerProperty, value); }
        }

        // Since the timer resolution defaults to about 10ms precisely, we need to
        // increase the resolution to get framerates above between 50fps with any
        // consistency.
        [DllImport("Winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern int TimeBeginPeriod(uint period);

        private void RestoreWindowState()
        {
            // Restore window state to that last used
            Rect bounds = Properties.Settings.Default.PrevWinPosition;
            if (bounds.Right != bounds.Left)
            {
                this.Top = bounds.Top;
                this.Left = bounds.Left;
                this.Height = bounds.Height;
                this.Width = bounds.Width;
            }

            this.WindowState = (WindowState)Properties.Settings.Default.WindowState;
        }

        private void WindowLoaded(object sender, EventArgs e)
        {
            playfield.ClipToBounds = true;
            
            this.UpdatePlayfieldSize();
            
            TimeBeginPeriod(TimerResolution);
            var myGameThread = new Thread(this.GameThread);
            myGameThread.SetApartmentState(ApartmentState.STA);
            myGameThread.Start();

            this.myMap.Dispatcher.Invoke(new Action(() => { this.myMap.SetView(new Microsoft.Maps.MapControl.WPF.Location(52.520, 13.4050), defaultZoom); }));
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            sensorChooser.Stop();

            this.runningGameThread = false;
            Properties.Settings.Default.PrevWinPosition = this.RestoreBounds;
            Properties.Settings.Default.WindowState = (int)this.WindowState;
            Properties.Settings.Default.Save();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            this.KinectSensorManager.KinectSensor = null;
        }

        #endregion ctor + Window Events

        #region Kinect discovery + setup

        private void KinectSensorChanged(object sender, KinectSensorManagerEventArgs<KinectSensor> args)
        {
            if (null != args.OldValue)
            {
                this.UninitializeKinectServices(args.OldValue);
            }

            // Only enable this checkbox if we have a sensor
            enableAec.IsEnabled = null != args.NewValue;

            if (null != args.NewValue)
            {
                this.InitializeKinectServices(this.KinectSensorManager, args.NewValue);
            }
        }

        // Kinect enabled apps should customize which Kinect services it initializes here.
        private void InitializeKinectServices(KinectSensorManager kinectSensorManager, KinectSensor sensor)
        {
            // Application should enable all streams first.
            kinectSensorManager.ColorFormat = ColorImageFormat.RgbResolution640x480Fps30;
            kinectSensorManager.ColorStreamEnabled = true;

            sensor.SkeletonFrameReady += this.SkeletonsReady;
            kinectSensorManager.TransformSmoothParameters = new TransformSmoothParameters
                                             {
                                                 Smoothing = 0.5f,
                                                 Correction = 0.5f,
                                                 Prediction = 0.5f,
                                                 JitterRadius = 0.05f,
                                                 MaxDeviationRadius = 0.04f
                                             };
            kinectSensorManager.SkeletonStreamEnabled = true;
            kinectSensorManager.KinectSensorEnabled = true;

            if (!kinectSensorManager.KinectSensorAppConflict)
            {
                // Start speech recognizer after KinectSensor started successfully.
                this.mySpeechRecognizer = SpeechRecognizer.Create();

                if (null != this.mySpeechRecognizer)
                {
                    this.mySpeechRecognizer.SaidSomething += this.RecognizerSaidSomething;
                    this.mySpeechRecognizer.Start(sensor.AudioSource);
                }

                enableAec.Visibility = Visibility.Visible;
                this.UpdateEchoCancellation(this.enableAec);
            }
        }

        // Kinect enabled apps should uninitialize all Kinect services that were initialized in InitializeKinectServices() here.
        private void UninitializeKinectServices(KinectSensor sensor)
        {
            sensor.SkeletonFrameReady -= this.SkeletonsReady;

            if (null != this.mySpeechRecognizer)
            {
                this.mySpeechRecognizer.Stop();
                this.mySpeechRecognizer.SaidSomething -= this.RecognizerSaidSomething;
                this.mySpeechRecognizer.Dispose();
                this.mySpeechRecognizer = null;
            }

            enableAec.Visibility = Visibility.Collapsed;
        }

        #endregion Kinect discovery + setup

        #region Kinect Skeleton processing
        private void SkeletonsReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    int skeletonSlot = 0;

                    if ((this.skeletonData == null) || (this.skeletonData.Length != skeletonFrame.SkeletonArrayLength))
                    {
                        this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    }

                    skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                    foreach (Skeleton skeleton in this.skeletonData)
                    {
                        if (SkeletonTrackingState.Tracked == skeleton.TrackingState)
                        {
                            Player player;
                            if (this.players.ContainsKey(skeletonSlot))
                            {
                                player = this.players[skeletonSlot];
                            }
                            else
                            {
                                player = new Player(skeletonSlot);
                                player.SetBounds(this.playerBounds);
                                this.players.Add(skeletonSlot, player);
                            }

                            player.LastUpdated = DateTime.Now;

                            // Update player's bone and joint positions
                            if (skeleton.Joints.Count > 0)
                            {
                                player.IsAlive = true;

                                // Head, hands, feet (hit testing happens in order here)
                                player.UpdateJointPosition(skeleton.Joints, JointType.Head);
                                player.UpdateJointPosition(skeleton.Joints, JointType.HandLeft);
                                player.UpdateJointPosition(skeleton.Joints, JointType.HandRight);
                                player.UpdateJointPosition(skeleton.Joints, JointType.FootLeft);
                                player.UpdateJointPosition(skeleton.Joints, JointType.FootRight);

                                // Hands and arms
                                player.UpdateBonePosition(skeleton.Joints, JointType.HandRight, JointType.WristRight);
                                player.UpdateBonePosition(skeleton.Joints, JointType.WristRight, JointType.ElbowRight);
                                player.UpdateBonePosition(skeleton.Joints, JointType.ElbowRight, JointType.ShoulderRight);

                                player.UpdateBonePosition(skeleton.Joints, JointType.HandLeft, JointType.WristLeft);
                                player.UpdateBonePosition(skeleton.Joints, JointType.WristLeft, JointType.ElbowLeft);
                                player.UpdateBonePosition(skeleton.Joints, JointType.ElbowLeft, JointType.ShoulderLeft);

                                // Head and Shoulders
                                player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderCenter, JointType.Head);
                                player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderLeft, JointType.ShoulderCenter);
                                player.UpdateBonePosition(skeleton.Joints, JointType.ShoulderCenter, JointType.ShoulderRight);

                                // Legs
                                player.UpdateBonePosition(skeleton.Joints, JointType.HipLeft, JointType.KneeLeft);
                                player.UpdateBonePosition(skeleton.Joints, JointType.KneeLeft, JointType.AnkleLeft);
                                player.UpdateBonePosition(skeleton.Joints, JointType.AnkleLeft, JointType.FootLeft);

                                player.UpdateBonePosition(skeleton.Joints, JointType.HipRight, JointType.KneeRight);
                                player.UpdateBonePosition(skeleton.Joints, JointType.KneeRight, JointType.AnkleRight);
                                player.UpdateBonePosition(skeleton.Joints, JointType.AnkleRight, JointType.FootRight);

                                player.UpdateBonePosition(skeleton.Joints, JointType.HipLeft, JointType.HipCenter);
                                player.UpdateBonePosition(skeleton.Joints, JointType.HipCenter, JointType.HipRight);

                                // Spine
                                player.UpdateBonePosition(skeleton.Joints, JointType.HipCenter, JointType.ShoulderCenter);
                            }
                            HandleUseCases(player, skeleton);
                        }

                        skeletonSlot++;
                    }
                }
            }
        }

        private void HandleUseCases(Player player, Skeleton skeleton)
        {
            player.UpdateUseCaseMode(this.screenRect, skeleton.Joints);
            if(player.Mode == Player.Playermode.Zoom)
                currentSetZoom += player.ZoomChange;

            cursorX = (int)(Application.Current.MainWindow.Left + player.LeftHandSegment.X1);
            cursorY = (int)(Application.Current.MainWindow.Top + player.LeftHandSegment.Y1);

            TravelMode newTransportMode;
            if (player.RightHandSegment.X1 > player.LeftHandSegment.X1)
            {
                if (player.RightHandSegment.Y1 > player.LeftHandSegment.Y1)
                {
                    newTransportMode = TravelMode.Bike;
                }
                else
                {
                    newTransportMode = TravelMode.Car;
                }

            }
            else
            {
                if (player.RightHandSegment.Y1 > player.LeftHandSegment.Y1)
                {
                    newTransportMode = TravelMode.Walk;
                }
                else
                {
                    newTransportMode = TravelMode.PublicTransport;
                }
            }
            if (newTransportMode != transportMode)
            {
                transportMode = newTransportMode;
                this.travelMode.Content = "Transport Mode:" + transportMode;
            }


            if (lastPlayerMode != player.Mode)
            {
                if (player.Mode == Player.Playermode.Pan)
                {
                    // Entering pan
                    NativeMethods.SendMouseInput(cursorX, cursorY, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, true, this.myMap);
                }

                if (player.Mode != Player.Playermode.Pan)
                {
                    // Exiting pan
                    NativeMethods.SendMouseInput(cursorX, cursorY, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, false, this.myMap);

                }
            }

            NativeMethods.SendMouseInput(cursorX, cursorY, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, player.Mode == Player.Playermode.Pan, this.myMap);


            lastPlayerMode = player.Mode;

            if (Math.Abs(defaultZoom - this.currentSetZoom) > 0.0001)
            {
                this.myMap.Dispatcher.Invoke(new Action(() => {
                    this.myMap.ZoomLevel = currentSetZoom;

                }));
                defaultZoom = currentSetZoom;
            }

        }

        private void CheckPlayers()
        {
            foreach (var player in this.players)
            {
                if (!player.Value.IsAlive)
                {
                    this.players.Remove(player.Value.GetId());
                    break;
                }
            }
        }

        private void PlayfieldSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdatePlayfieldSize();
        }

        private void UpdatePlayfieldSize()
        {
            this.screenRect.X = 0;
            this.screenRect.Y = 0;
            this.screenRect.Width = this.playfield.ActualWidth;
            this.screenRect.Height = this.playfield.ActualHeight;
            

            this.playerBounds.X = 0;
            this.playerBounds.Width = this.playfield.ActualWidth;
            this.playerBounds.Y = this.playfield.ActualHeight * 0.2;
            this.playerBounds.Height = this.playfield.ActualHeight * 0.75;

            foreach (var player in this.players)
            {
                player.Value.SetBounds(this.playerBounds);
            }

            Rect fallingBounds = this.playerBounds;
            fallingBounds.Y = 0;
            fallingBounds.Height = playfield.ActualHeight;
        }
        #endregion Kinect Skeleton processing

        #region GameTimer/Thread
        private void GameThread()
        {
            this.runningGameThread = true;
            this.predNextFrame = DateTime.Now;
            this.actualFrameTime = 1000.0 / this.targetFramerate;
            
            while (this.runningGameThread)
            {
                DateTime now = DateTime.Now;
                if (this.lastFrameDrawn == DateTime.MinValue)
                {
                    this.lastFrameDrawn = now;
                }

                double ms = now.Subtract(this.lastFrameDrawn).TotalMilliseconds;
                this.actualFrameTime = (this.actualFrameTime * 0.95) + (0.05 * ms);
                this.lastFrameDrawn = now;

                // Adjust target framerate down if we're not achieving that rate
                this.frameCount++;
                if ((this.frameCount % 100 == 0) && (1000.0 / this.actualFrameTime < this.targetFramerate * 0.92))
                {
                    this.targetFramerate = Math.Max(MinFramerate, (this.targetFramerate + (1000.0 / this.actualFrameTime)) / 2);
                }

                if (now > this.predNextFrame)
                {
                    this.predNextFrame = now;
                }
                else
                {
                    double milliseconds = this.predNextFrame.Subtract(now).TotalMilliseconds;
                    if (milliseconds >= TimerResolution)
                    {
                        Thread.Sleep((int)(milliseconds + 0.5));
                    }
                }

                this.predNextFrame += TimeSpan.FromMilliseconds(1000.0 / this.targetFramerate);

                this.Dispatcher.Invoke(DispatcherPriority.Send, new Action<int>(this.HandleGameTimer), 0);
            }
        }

        private void HandleGameTimer(int param)
        {

            this.zoomlevel.Content = "Zoomlevel: " + this.defaultZoom;
            this.panStatus.Content = "X:" + cursorX + "\nY: " + cursorY;
            
            // Draw new Wpf scene by adding all objects to canvas
            playfield.Children.Clear();
            foreach (var player in players)
            {
                player.Value.Draw(playfield.Children);
            }
            
            FlashingText.Draw(playfield.Children);

            this.CheckPlayers();
        }
        #endregion GameTimer/Thread

        #region Kinect Speech processing
        private void RecognizerSaidSomething(object sender, SpeechRecognizer.SaidSomethingEventArgs e)
        {
            switch (e.Verb)
            {
                case SpeechRecognizer.Verbs.GoToPlace:
                    FlashingText.NewFlyingText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), 
                        "Go to " + e.Place);
                    defaultZoom = 10;
                    currentSetZoom = 10;
                    this.myMap.Dispatcher.Invoke(new Action(() => { this.myMap.SetView(new Location(e.Longitude, e.Latitude), defaultZoom); }));
                    latitude = e.Latitude;
                    longitude = e.Longitude;
                    break;
                case SpeechRecognizer.Verbs.NavigateTo:
                    FlashingText.NewFlyingText(this.screenRect.Width / 30, new Point(this.screenRect.Width / 2, this.screenRect.Height / 2), 
                        "Navigate to " + e.Place + " using " + transportMode);
                    defaultZoom = 10;
                    currentSetZoom = 10;
                    Navigate(this.myMap.Center, new Location(e.Longitude, e.Latitude));
                    break;
                case SpeechRecognizer.Verbs.Finish:
                    System.Diagnostics.Debug.Write("Finish");
                    Application.Current.MainWindow.Close();
                    break;
            }
        }

        private void EnableAecChecked(object sender, RoutedEventArgs e)
        {
            var enableAecCheckBox = (CheckBox)sender;
            this.UpdateEchoCancellation(enableAecCheckBox);
        }

        private void UpdateEchoCancellation(CheckBox aecCheckBox)
        {
            this.mySpeechRecognizer.EchoCancellationMode = aecCheckBox.IsChecked != null && aecCheckBox.IsChecked.Value
                ? EchoCancellationMode.CancellationAndSuppression
                : EchoCancellationMode.None;
        }

        #endregion Kinect Speech processing

        

        private BingServices.GeocodeResult GeocodeAddress(string address)
        {
            BingServices.GeocodeResult result = null;

            using (BingServices.GeocodeServiceClient client = new BingServices.GeocodeServiceClient("CustomBinding_IGeocodeService"))
            {
                BingServices.GeocodeRequest request = new BingServices.GeocodeRequest();
                request.Credentials = new Credentials() { ApplicationId = (App.Current.Resources["MyCredentials"] as ApplicationIdCredentialsProvider).ApplicationId };
                request.Query = address;

                result = client.Geocode(request).Results[0];
            }

            return result;
        }

        private BingServices.RouteResult CalculateRoute(Location from, Location to)
        {
            using (BingServices.RouteServiceClient client = new BingServices.RouteServiceClient("CustomBinding_IRouteService"))
            {

                BingServices.RouteRequest request = new BingServices.RouteRequest();

                request.Credentials = new Credentials() { ApplicationId = (App.Current.Resources["MyCredentials"] as ApplicationIdCredentialsProvider).ApplicationId };
                Collection<BingServices.Waypoint> waypoints = new ObservableCollection<BingServices.Waypoint>();

                waypoints.Add(ConvertResultToWayPoint(from));
                waypoints.Add(ConvertResultToWayPoint(to));

                request.Waypoints = waypoints.ToArray();
                request.Options = new BingServices.RouteOptions();
                request.Options.RoutePathType = BingServices.RoutePathType.Points;
                if (transportMode == TravelMode.Walk || transportMode == TravelMode.Bike)
                {
                    request.Options.Mode = BingServices.TravelMode.Walking;
                }
                else
                {
                    request.Options.Mode = BingServices.TravelMode.Driving;
                }

                return client.CalculateRoute(request).Result;
            }
        }
        public void ShowRouteOnMap(BingServices.RouteResult newValue)
        {
            MapPolyline routeLine = new MapPolyline();
            routeLine.Locations = new LocationCollection();
            routeLine.Opacity = 0.65;
            routeLine.Stroke = new SolidColorBrush(Colors.Blue);
            routeLine.StrokeThickness = 5.0;

            foreach (BingServices.Location loc in newValue.RoutePath.Points)
            {
                routeLine.Locations.Add(new Location(loc.Latitude, loc.Longitude));
            }

            var routeLineLayer = RouteLineLayer;
            if (routeLineLayer == null)
            {
                routeLineLayer = new MapLayer();
                SetRouteLineLayer(myMap, routeLineLayer);
            }

            routeLineLayer.Children.Clear();
            routeLineLayer.Children.Add(routeLine);

            //Set the map view
            LocationRect rect = new LocationRect(routeLine.Locations[0], routeLine.Locations[routeLine.Locations.Count - 1]);
            myMap.SetView(rect);
            this.myMap.Dispatcher.Invoke(new Action(() => {
                myMap.ZoomLevel -= 1;
                defaultZoom = (float)myMap.ZoomLevel;
                currentSetZoom = defaultZoom;
            }));
        }

        public void SetRouteLineLayer(DependencyObject target, MapLayer value)
        {
            if (!myMap.Children.Contains(value))
                myMap.Children.Add(value);
        }


        private static string GetDirectionText(BingServices.ItineraryItem item)
        {
            string contentString = item.Text;
            //Remove tags from the string
            Regex regex = new Regex("< (.|\n) *?>");
            contentString = regex.Replace(contentString, string.Empty);
            return contentString;
        }

        private BingServices.Waypoint ConvertResultToWayPoint(BingServices.GeocodeResult result)
        {
            BingServices.Waypoint waypoint = new BingServices.Waypoint();
            waypoint.Description = result.DisplayName;
            waypoint.Location = result.Locations[0];
            return waypoint;
        }
        private BingServices.Waypoint ConvertResultToWayPoint(Location result)
        {
            BingServices.Waypoint waypoint = new BingServices.Waypoint();
            waypoint.Location = new BingServices.Location();
            waypoint.Location.Longitude = result.Longitude;
            waypoint.Location.Latitude = result.Latitude;
            return waypoint;
        }
        public class Direction
        {
            public string Description { get; set; }
            public Location Location { get; set; }
        }
    }
}
