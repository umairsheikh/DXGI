using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Lidgren.Network;
using MirrorDriver;
using Model.LiveControl;
using Network;
using Network.Messages.LiveControl;
using Providers.Extensions;
using Point = System.Drawing.Point;
using Model.Extensions;
using Rectangle = System.Drawing.Rectangle;
using DesktopDuplication;

namespace Providers.LiveControl.Server
{

    class LiveControllerProvider8 :Provider
    {

        private DesktopDuplicator MirrorDriver;
        public DesktopFrame frame = null;

        /// <summary>
        /// Stores a list of screen regions that have changed, to be optimized for later.
        /// </summary>
        private List<Rectangle> DesktopChanges { get; set; }

        private Stopwatch Timer { get; set; }
        public uint ScreenshotCounter = 0;
        public static int mtu = 250;

        public LiveControllerProvider8(NetworkPeer network)
            : base(network)
        {

            try
            {
                MirrorDriver = new DesktopDuplicator(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            //DesktopChanges = new List<Rectangle>();
            //Timer = new Stopwatch();
            //MirrorDriver.DesktopChange += new EventHandler<DesktopMirror.DesktopChangeEventArgs>(MirrorDriver_DesktopChange);
        }

        public override void RegisterMessageHandlers()
        {
            Network.RegisterMessageHandler<RequestScreenshotMessage>(OnRequestScreenshotMessageReceived);

            while (true)
            {
                Application.DoEvents();
                
                try
                {
                    frame = MirrorDriver.GetLatestFrame();
                }
                catch
                {
                    MirrorDriver = new DesktopDuplicator(0);
                    continue;
                }

                if (frame != null)
                {
                    //LabelCursor.Location = frame.CursorLocation;
                    //LabelCursor.Visible = frame.CursorVisible;

                    //Debug.WriteLine("--------------------------------------------------------");
                    //foreach (var moved in frame.MovedRegions)
                    //{
                    //    Debug.WriteLine(String.Format("Moved: {0} -> {1}", moved.Source, moved.Destination));
                    //    MovedRegion.Location = moved.Destination.Location;
                    //    MovedRegion.Size = moved.Destination.Size;
                    //}
                    //foreach (var updated in frame.UpdatedRegions)
                    //{
                    //    Debug.WriteLine(String.Format("Updated: {0}", updated.ToString()));
                    //    UpdatedRegion.Location = updated.Location;
                    //    UpdatedRegion.Size = updated.Size;
                    //}

                    //this.BackgroundImage = frame.DesktopImage;
                }
            }
        }

        private void MirrorDriver_DesktopChange(object sender, DesktopMirror.DesktopChangeEventArgs e)
        {
            var rectangle = new Rectangle(e.Region.x1, e.Region.y1, e.Region.x2 - e.Region.x1,
                                                e.Region.y2 - e.Region.y1);
            DesktopChanges.Add(rectangle);
        }

        public bool DoesMirrorDriverExist()
        {
            return true;//MirrorDriver.DriverExists();
        }

        //on screen shared initiated
        private void OnRequestScreenshotMessageReceived(MessageEventArgs<RequestScreenshotMessage> e)
        {


            //if (MirrorDriver.State != DesktopMirror.MirrorState.Running)
            /*  while(true)
              {
                  // Most likely first time
                  // Start the mirror driver
                  //MirrorDriver.Load();

                  //MirrorDriver.Connect();
                  //MirrorDriver.Start();



                  //Bitmap screenshot = MirrorDriver.GetScreen();
                  var frame = MirrorDriver.GetLatestFrame();
                  var screenshot = frame.DesktopImage;
                  var stream = new MemoryStream();
                  screenshot.Save(stream, ImageFormat.Png);

                  SendFragmentedBitmap(stream.ToArray(), Screen.PrimaryScreen.Bounds);
              }*/
            //else if (MirrorDriver.State == DesktopMirror.MirrorState.Running)
            while(true)
            {
                Trace.WriteLine(String.Format("Received RequestScreenshotMessage."));

                // Send them the list of queued up changes
                var regions = (List<Rectangle>)GetOptimizedRectangleRegions();

                Bitmap screenshot = MirrorDriver.GetLatestFrame().DesktopImage;
                
                if (regions.Count == 0)
                {
                    Network.SendMessage(new ResponseEmptyScreenshotMessage(), NetDeliveryMethod.ReliableOrdered, 0);
                    return;
                }

                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i].IsEmpty) continue;

                    Bitmap regionShot = null;

                    try
                    {
                        regionShot = screenshot.Clone(regions[i], PixelFormat.Format16bppRgb565);
                    }
                    catch (OutOfMemoryException ex)
                    {
                        Trace.WriteLine("OutOfMemoryException");
                    }

                    var stream = new MemoryStream();
                    regionShot.Save(stream, ImageFormat.Png);

                    SendFragmentedBitmap(stream.ToArray(), regions[i]);
                }
            }
        }

        private void SendFragmentedBitmap(byte[] bitmapBytes, Rectangle region)
        {
            // Send ResponseBeginScreenshotMessage
            var beginMessage = new ResponseBeginScreenshotMessage();
            beginMessage.Number = ++ScreenshotCounter;
            beginMessage.Region = region;
            beginMessage.FinalLength = bitmapBytes.Length;

            Network.SendMessage(beginMessage, NetDeliveryMethod.ReliableOrdered, 0);

            // Send ResponseScreenshotMessage

            // We don't want to send a 300 KB image - we want to make each packet mtu bytes
            // int numFragments = (bitmapBytes.Length / mtu) + 1;
            int numFragments = ((int)Math.Floor((decimal)bitmapBytes.Length / (decimal)mtu)) + 1;

            for (int i = 0; i < numFragments; i++)
            {
                var message = new ResponseScreenshotMessage();

                byte[] regionFragmentBuffer = null;

                if (i != numFragments - 1 && i < numFragments)
                {
                    regionFragmentBuffer = new byte[mtu];
                    Buffer.BlockCopy(bitmapBytes, i * mtu, regionFragmentBuffer, 0, mtu);
                }
                else if (i == numFragments - 1 || numFragments == 1)
                {
                    int bytesLeft = (int)(bitmapBytes.Length % mtu);
                    regionFragmentBuffer = new byte[bytesLeft];
                    Buffer.BlockCopy(bitmapBytes, i * mtu, regionFragmentBuffer, 0, bytesLeft);
                }
                else if (i == numFragments - 1)
                {
                    break;
                }

                if (regionFragmentBuffer == null)
                    Debugger.Break();

                message.Number = ScreenshotCounter;
                message.Image = regionFragmentBuffer;
                message.SendIndex = (i <= 0) ? (0) : (i);
                Network.SendMessage(message, NetDeliveryMethod.ReliableOrdered, 0);
                Trace.WriteLine(String.Format("Sent screenshot #{0}, fragment #{1} of {2} ({3} KB).", ScreenshotCounter, i, numFragments, message.Image.Length.ToKilobytes()));
            }

            Network.SendMessage(new ResponseEndScreenshotMessage() { Number = ScreenshotCounter });
            Trace.WriteLine(String.Format("Completed send of screenshot #{0}, Size: {1} KB", ScreenshotCounter, bitmapBytes.Length.ToKilobytes()));
        }

        private static float GetTotalScreenshotsKB(List<Screenshot> screenshots)
        {
            float total = 0f;
            screenshots.ForEach(x =>
            {
                total += x.Image.Length.ToKilobytes();
            });
            return total;
        }

        /// <summary>
        /// Combines intersecting rectangles to reduce redundant sends.
        /// </summary>
        /// <returns></returns>
        public IList<Rectangle> GetOptimizedRectangleRegions()
        {
            var desktopChangesCopy = new List<Rectangle>(DesktopChanges);
            DesktopChanges.Clear();

            desktopChangesCopy.ForEach((x) => desktopChangesCopy.ForEach((y) =>
            {
                if (x != y && x.Contains(y))
                {
                    desktopChangesCopy.Remove(y);
                }
            }));

            return desktopChangesCopy;
        }

        public event EventHandler<DesktopChangedEventArgs> OnDesktopChanged;




    }
}
