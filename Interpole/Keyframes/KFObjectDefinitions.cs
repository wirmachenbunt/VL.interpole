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
        /*
        public class Track : UIelement
        {
            public List<Keyframe> KeyframeList { get; set; }
        }

        public class Keyframe : UIelement
        {
            dynamic value { get; set; }
        }
        */


        class Keyframe1D : IKeyFrame
        {
            public float value { get; set; }
        }

        class Keyframe2D : IKeyFrame
        {
            public Vector2D value { get; set; }
        }

        public interface IKeyFrame { }

        public class Track
        {
            public List<IKeyFrame> Frames { get; set; }

        }

        //
        // just a demo to test the track object
        //
        public static void demo()
        {
            Track mytrack = new Track();

            var frame = new Keyframe1D();
            frame.value = 100;

            mytrack.Frames.Add(frame);

        }
    }
}


