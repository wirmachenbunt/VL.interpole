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

        public void AddClip()
        {
            string input = "MyClip";
            int startFrame = 0;
            int endFrame = 60;

            if (ClipDialog(ref input, ref startFrame, ref endFrame, "Add Clip", dpi) == DialogResult.OK)
            {
                //add state to state list
                clipList.Add(new Clip()
                {
                    ID = RNGCharacterMask(),
                    Name = input,
                    StartFrame = startFrame,
                    EndFrame = endFrame,
                    Bounds = new Rectangle(mousePos, new Size(100, 20))
                });
            }
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
