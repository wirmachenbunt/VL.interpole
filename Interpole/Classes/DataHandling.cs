using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Drawing;

namespace VVVV.Nodes
{
    partial class Interpole
    {
        public List<Clip> clipList = new List<Clip>();
        int playPosFrames = 20;
        
        public void AddClip()
        {
            string input = "MyClip";
            int startFrame = mousePos.X;
            int duration = 60;
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
                    Bounds = new Rectangle(new Point(startFrame,mousePos.Y), new Size(duration, 20)),
                    Color = colorName
                });

                lastColorName = colorName;
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
                clip.Bounds = new Rectangle(new Point(startFrame, clip.Bounds.Location.Y), new Size(duration, 20));
                lastColorName = colorName;
            }
        }

        public void DragClip(Clip clip, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Form.ModifierKeys == Keys.Control)
            {
                int dragHorizontal = clip.Bounds.Location.X + (mousePos.X - deltaMousePos.X);
                clip.Bounds = new Rectangle(new Point(dragHorizontal, clip.Bounds.Location.Y), clip.Bounds.Size);
                clip.StartFrame = dragHorizontal;
            }

            if (e.Button == MouseButtons.Right && Form.ModifierKeys == Keys.Control)
            {
                int dragVertical = clip.Bounds.Location.Y + (mousePos.Y - deltaMousePos.Y);
                clip.Bounds = new Rectangle(new Point(clip.Bounds.Location.X, dragVertical), clip.Bounds.Size);
            }
        }

        public void DeleteClip(Clip clip)
        {
            clipList.Remove(clip);
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
