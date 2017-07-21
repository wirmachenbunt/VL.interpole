using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Drawing;
using System.Xml.Serialization;
using System.IO;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        public List<Clip> clipList = new List<Clip>();
        int playPosFrames = 100;
        
        public void AddClip()
        {
            string input = "MyClip";
            int startFrame = mousePos.X*timeZoom;
            int duration = 160;
            string colorName = lastColorName;

            if (ClipDialog(ref input, ref startFrame, ref duration, ref colorName, "Add Clip", dpi) == DialogResult.OK)
            {
                //int duration = duration;
                
                //add state to state list
                clipList.Add(new Clip()
                {
                    ID = RNGCharacterMask(),
                    Name = input,
                    StartFrame = startFrame,
                    Duration = duration,
                    Bounds = new Rectangle(new Point(startFrame/timeZoom,mousePos.Y), new Size(duration/timeZoom, 20)),
                    Color = colorName
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

            if (ClipDialog(ref input, ref startFrame, ref duration, ref colorName, "Edit Clip", dpi) == DialogResult.OK)
            {
                clip.Name = input;
                clip.StartFrame = startFrame;
                clip.Duration = duration;
                clip.Color = colorName;
                clip.Bounds = new Rectangle(new Point(startFrame/timeZoom, clip.Bounds.Location.Y), new Size(duration/timeZoom, 20));
                lastColorName = colorName;
                UpdateClipXML();
            }
        }

        public void DragClip(Clip clip, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Form.ModifierKeys == Keys.Control)
            {
                int dragHorizontal = clip.Bounds.Location.X + (mousePos.X - deltaMousePos.X);
                clip.Bounds = new Rectangle(new Point(dragHorizontal, clip.Bounds.Location.Y), clip.Bounds.Size);
                clip.StartFrame = dragHorizontal*timeZoom;
                UpdateClipXML();
            }

            if (e.Button == MouseButtons.Right && Form.ModifierKeys == Keys.Control)
            {
                int dragVertical = clip.Bounds.Location.Y + (mousePos.Y - deltaMousePos.Y);
                clip.Bounds = new Rectangle(new Point(clip.Bounds.Location.X, dragVertical), clip.Bounds.Size);
                UpdateClipXML();
            }
        }

        public void DeleteClip(Clip clip)
        {
            clipList.Remove(clip);
            UpdateClipXML();
        }

        private void UpdateClipXML()
        {

            ClipXML[0] = SerializeClipList(clipList); //save config
            //StatesChanged = true;
        }

        public static string SerializeClipList(List<Clip> myList)
        {
            StringWriter sw = new StringWriter();
            XmlSerializer s = new XmlSerializer(myList.GetType());
            s.Serialize(sw, myList);
            return sw.ToString();
        }

        public static List<Clip> DeserializeClipList(string data)
        {
            XmlSerializer xs = new XmlSerializer(typeof(List<Clip>));
            List<Clip> newList = (List<Clip>)xs.Deserialize(new StringReader(data));
            return newList;
        }

        public string Frames2Timecode(int frameCount, int frameRate)
        {       
            int Frames = frameCount % frameRate;
            int Seconds = (int)Math.Floor(frameCount / (double)frameRate) % 60;
            int Minutes = (int)Math.Floor(frameCount / (frameRate * 60d)) % 60;
            int Hours = (int)Math.Floor(frameCount / (frameRate * 60 * 60d)) % 24;     
            return $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}{":"}{Frames:D2}";
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

       
    }
}
