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
    [PluginInfo(Name = "SplitTC",
                Category = "Clip Animation",
                Help = "split timecode values",
                Tags = "",
                AutoEvaluate = false)]
    #endregion PluginInfo
    public class SplitTC: IPluginEvaluate
    {
        #region fields & pins    

        [Input("Timecode")]
        public IDiffSpread<Interpole.TimeCode> FTimeCode;

        [Output("Hours")]
        public ISpread<int> FHours;

        [Output("Minutes")]
        public ISpread<int> FMinutes;

        [Output("Seconds")]
        public ISpread<int> FSeconds;

        [Output("Frames")]
        public ISpread<int> FFrames;

        

        [Import()]
        public ILogger FLogger;

        Interpole.TimeCode mytimecode = new Interpole.TimeCode();

        #endregion fields & pins


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (FTimeCode.IsChanged && FTimeCode[0] != null)
            {
                FHours[0] = FTimeCode[0].Hours;
                FMinutes[0] = FTimeCode[0].Minutes;
                FSeconds[0] = FTimeCode[0].Seconds;
                FFrames[0] = FTimeCode[0].Frames;
            }
        }
    }
}
