using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Drawing;
using System.Xml.Serialization;
using System.IO;
using VVVV.Core.Logging;
using System.Diagnostics;

namespace Interpole
{
    partial class InterpoleForm
    {
        
        public void AddClip()
        {
            string input = "MyClip";
            int startFrame = mousePos.X * timeZoom;
            int duration = 160;
            int fadeIn = 0;
            int fadeOut = 0;
            string colorName = lastColorName;
            string variable = "string variable";

            PushUndoStep();

            if (ClipDialog(ref input, ref startFrame, ref duration, ref fadeIn, ref fadeOut, ref colorName, ref variable, "Add Clip", dpi) == DialogResult.OK)
            {
                //int duration = duration;

                //add state to state list
                ipD.ClipList.Add(new Clip()
                {
                    ID = RNGCharacterMask(),
                    Name = input,
                    StartFrame = startFrame,
                    Duration = duration,
                    FadeIn = fadeIn,
                    FadeOut = fadeOut,
                    Bounds = new Rectangle(new Point(startFrame / timeZoom, mousePos.Y), new Size(duration / timeZoom, 20)),
                    Color = colorName,
                    Variable = variable
                });

                lastColorName = colorName;
                UpdateClipXML();
            }
        }

        public void EditClip(Clip clip)
        {
            string input = clip.Name;
            int startFrame = clip.StartFrame;
            int duration = clip.Duration;
            string colorName = clip.Color;
            int fadeIn = clip.FadeIn;
            int fadeOut = clip.FadeOut;
            string variable = clip.Variable;

            PushUndoStep();

            if (ClipDialog(ref input, ref startFrame, ref duration, ref fadeIn, ref fadeOut, ref colorName, ref variable, "Edit Clip", dpi) == DialogResult.OK)
            {
                clip.Name = input;
                clip.StartFrame = startFrame;
                clip.Duration = duration;
                clip.FadeIn = fadeIn;
                clip.FadeOut = fadeOut;
                clip.Color = colorName;
                clip.Variable = variable;
                clip.Bounds = new Rectangle(new Point(startFrame / timeZoom, clip.Bounds.Location.Y), new Size(duration / timeZoom, 20));
                lastColorName = colorName;
                UpdateClipXML();
            }
        }

        public void DragSelection(System.Windows.Forms.MouseEventArgs e)
        {
            // standard horizontal drag
            if (e.Button == MouseButtons.Left)
            {

                foreach (Clip myclip in selectionList)
                {
                    int dragHorizontal = myclip.Bounds.Location.X + (mousePos.X - deltaMousePos.X);
                    if (dragHorizontal < 0) dragHorizontal = 0;
                    myclip.Bounds = new Rectangle(new Point(dragHorizontal, myclip.Bounds.Location.Y), myclip.Bounds.Size);
                    myclip.StartFrame = dragHorizontal * timeZoom;
                    rememberLastFunction = "dragclip";
                    //Invalidate(); //redraw
                }
            }


            // standard vertical drag
            if (e.Button == MouseButtons.Right)
            {
                // standard drag
                foreach (Clip myclip in selectionList)
                {
                    int dragVertical = myclip.Bounds.Location.Y + (mousePos.Y - deltaMousePos.Y);
                    if (dragVertical < 0) dragVertical = 0;
                    myclip.Bounds = new Rectangle(new Point(myclip.Bounds.Location.X, dragVertical), myclip.Bounds.Size);
                    rememberLastFunction = "dragclip";
                    //Invalidate(); //redraw
                }
            }

            // snap first clip to playhead function
            if (Form.ModifierKeys == Keys.Shift)
            {
                Clip firstClip = selectionList.OrderBy(n => n.StartFrame).First();
                int distance = firstClip.StartFrame - playPosFrames;
                if (Math.Abs(distance) < 20)
                {
                    //FLogger.Log(LogType.Debug, "within distance");
                    foreach (Clip myclip in selectionList)
                    {
                        myclip.StartFrame = myclip.StartFrame - distance;
                        myclip.Bounds = new Rectangle(new Point(myclip.StartFrame / timeZoom, myclip.Bounds.Location.Y), myclip.Bounds.Size);
                    }
                    rememberLastFunction = "dragclip";
                }
            }

        }

        public void DeleteClip(Clip clip)
        {
            PushUndoStep();
            ipD.ClipList.Remove(clip);
            UpdateClipXML();
        }

        public void EditTimeCode()
        {
            string input = Frames2Timecode(playPosFrames, frameRate, false);

            if (TimecodeDialog(ref input, "Edit Timecode") == DialogResult.OK)
            {
                playPosFrames = TimeCode2Frames(input, frameRate);
                StagePos.X = 0 - playPosFrames / timeZoom;
                //Invalidate();
            }

        }

        public void DragDuration(Clip myclip)
        {
            int start = (myclip.Bounds.X + myclip.Bounds.Width) - (10);
            int end = myclip.Bounds.X + myclip.Bounds.Width + 5;

            

            if (mousePos.X > start && mousePos.X < end)
            {
                if (rememberLastFunction == "idle") PushUndoStep();
                dragDurationClip = myclip;
            }

            if (dragDurationClip != null)
            {
                this.Cursor = Cursors.SizeWE;
                int dragHorizontal = myclip.Bounds.Width + (mousePos.X - deltaMousePos.X);
                myclip.Bounds = new Rectangle(myclip.Bounds.Location, new Size(dragHorizontal, myclip.Bounds.Height));


                myclip.Duration = dragHorizontal * timeZoom;
                rememberLastFunction = "dragclip";          
            }


            // snap first clip to playhead function
            if (Form.ModifierKeys == Keys.Shift && dragDurationClip != null)
            {
                
                int distance = (myclip.StartFrame + myclip.Duration) - playPosFrames;
                if (Math.Abs(distance) < 20)
                {
                    myclip.Duration = playPosFrames - myclip.StartFrame;

                    myclip.Bounds = new Rectangle(myclip.Bounds.Location, new Size(myclip.Duration/timeZoom, myclip.Bounds.Height));
              
                    rememberLastFunction = "dragclip";
                }
            }


        }

        public void PushUndoStep()
        {
            string tempXML = SerializeClipList(ipD.ClipList); //convert list of objects to string

            undoXML.Push(tempXML); //add undo save point to stack

            Debug.WriteLine("undoSteps:" + undoXML.Count.ToString());
        }

        private void UpdateClipXML()
        {
            /*
            string tempXML = SerializeClipList(clipList); //convert list of objects to string

            ClipXML[0] = tempXML; //save config

            needsUpdate = true; //inform getclip about change
            
            rememberLastFunction = "updateXML"; //remember action

            ClipXMLOut[0] = tempXML;

            //tty messages
            FLogger.Log(LogType.Debug, "Update XML");
            */
        }

        public static string SerializeClipList(List<Clip> myList)
        {
            StringWriter sw = new StringWriter();
            XmlSerializer s = new XmlSerializer(myList.GetType());
            s.Serialize(sw, myList);
            return sw.ToString();
        }

        public static string SerializeInterpoleData(InterpoleData myipD)
        {
            StringWriter sw = new StringWriter();
            XmlSerializer s = new XmlSerializer(myipD.GetType());
            s.Serialize(sw, myipD);
            return sw.ToString();
        }

        public static List<Clip> DeserializeClipList(string data)
        {
            XmlSerializer xs = new XmlSerializer(typeof(List<Clip>));
            List<Clip> newList = (List<Clip>)xs.Deserialize(new StringReader(data));
            return newList;
        }

        public string Frames2Timecode(int frameCount, int frameRate, bool isruler)
        {
            int Frames = frameCount % frameRate;
            int Seconds = (int)Math.Floor(frameCount / (double)frameRate) % 60;
            int Minutes = (int)Math.Floor(frameCount / (frameRate * 60d)) % 60;
            int Hours = (int)Math.Floor(frameCount / (frameRate * 60 * 60d)) % 24;
            if (isruler) return $"{Minutes:D2}:{Seconds:D2}";
            else return $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}{":"}{Frames:D2}";

        }

        public int TimeCode2Frames(string TimeCode, int frameRate)
        {
            int frameCount = 0;
            var numbers = TimeCode.Split(':').Select(Int32.Parse).ToList();

            if (numbers.Count == 4)
            {
                frameCount = numbers[3] + (numbers[2] * frameRate) + (numbers[1] * 60 * frameRate) + (numbers[0] * 60 * 60 * frameRate);
            }

            return frameCount;
        }

        public static string RNGCharacterMask()
        {
            int maxSize = 8;
            //int minSize = 5 ;
            char[] chars = new char[62];
            string a;
            a = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            chars = a.ToCharArray();
            int size = maxSize;
            byte[] data = new byte[1];
            RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider();
            crypto.GetNonZeroBytes(data);
            size = maxSize;
            data = new byte[size];
            crypto.GetNonZeroBytes(data);
            StringBuilder result = new StringBuilder(size);
            foreach (byte b in data)
            { result.Append(chars[b % (chars.Length - 1)]); }
            return result.ToString();
        }

        public void PasteClips()
        {
            Clip firstClip = clipBoard.OrderBy(n => n.StartFrame).First();
            int distance = firstClip.StartFrame - playPosFrames;

            PushUndoStep();

            foreach (Clip myclip in clipBoard)
            {
                //add state to state list
                ipD.ClipList.Add(new Clip()
                {
                    ID = RNGCharacterMask(),
                    Name = myclip.Name,
                    StartFrame = myclip.StartFrame - distance,
                    Duration = myclip.Duration,
                    FadeIn = myclip.FadeIn,
                    FadeOut = myclip.FadeOut,
                    Variable = myclip.Variable,
                    Bounds = new Rectangle(new Point(((myclip.StartFrame - distance) / timeZoom), myclip.Bounds.Y), myclip.Bounds.Size),
                    Color = myclip.Color
                });
            }
            UpdateClipXML();
        }

        public void CopyClips()
        {
            foreach (Clip myclip in selectionList)
            {
                clipBoard.Add(myclip);
            }

        }

        public void DeleteClipSelection()
        {
            PushUndoStep();
            foreach (Clip myclip in selectionList)
            {
                ipD.ClipList.Remove(myclip);
            }
            UpdateClipXML();
        }

    }
}
