#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using System.Linq;
using VVVV.Nodes;


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Join TC",
                Category = "Clip Animation",
                Help = "join timecode values",
                Tags = "",
                AutoEvaluate = false)]
    #endregion PluginInfo
    public class JoinTC : IPluginEvaluate
    {
        #region fields & pins    

        [Input("Hours")]
        public IDiffSpread<int> FHours;

        [Input("Minutes")]
        public IDiffSpread<int> FMinutes;

        [Input("Seconds")]
        public IDiffSpread<int> FSeconds;

        [Input("Frames")]
        public IDiffSpread<int> FFrames;

        [Output("Timecode")]
        public ISpread<Interpole.TimeCode> FTimeCode;

        [Import()]
        public ILogger FLogger;

        Interpole.TimeCode mytimecode = new Interpole.TimeCode();

        #endregion fields & pins


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if(FHours.IsChanged ||
                FMinutes.IsChanged ||
                FSeconds.IsChanged ||
                FFrames.IsChanged)
            {
                mytimecode.Hours = FHours[0];
                mytimecode.Minutes = FMinutes[0];
                mytimecode.Seconds = FSeconds[0];
                mytimecode.Frames = FFrames[0];

                FTimeCode[0] = mytimecode;
            }
        }
    }
}
