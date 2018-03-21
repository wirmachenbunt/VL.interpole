using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using VVVV.Utils.VMath;

namespace Interpole
{
    public partial class InterpoleBase
    {
        
        public class Track : UIelement
        {
            public string Name { get; set;}
            public TrackType Type { get; set; }
            public List<IOBox> IOBox { get; set; } 
            public List<Keyframe> KeyframeList { get; set; }
            public bool CatmullRom { get; set; }
        }

        public class IOBox:UIelement
        {
            public double Value { get; set; }
        }


        public class Keyframe : UIelement
        {
            dynamic Value { get; set; }
            Interpolation In { get; set; }
            Interpolation Out { get; set; }
        }
        
        public enum TrackType
        {
            Single,
            Vector2D,
            Vector3D,
            Vector4D,
            Color,
            String
        }
        
        public enum Interpolation
        {
            Linear,
            Ease
        }

        //
        // just a demo to test the track object
        //
        public static void demo()
        {
            Track myTrack = new Track();
            myTrack.Name = "meiner erster Track";
            myTrack.Type = TrackType.Single;

            myTrack.IOBox.Add(new IOBox()
            {
                Bounds = new Rectangle(0,0,10,10),
                Value = 0
            });
        }
    }
}


