#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;


using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "InterpolateTC",
                Category = "Clip Animation",
                Help = "Generate Timecode for network use",
                Tags = "c#")]
    #endregion PluginInfo
    public class InterpolateTC : IPluginEvaluate
    {
        #region fields & pins
        [Config("Framerate", DefaultValue = 50)]
        public IDiffSpread<int> Framerate;

        [Input("IsPlay")]
        public IDiffSpread<bool> FPlay;

        [Input("TimeCode H:M:S")]
        public IDiffSpread<string> FTCString;

        [Output("Timecode")]
        public ISpread<Interpole.TimeCode> FTimeCode;

        [Import()]
        public ILogger FLogger;

        Interpole.TimeCode mytimecode = new Interpole.TimeCode();
        string[] timecodeList;
        int counter = 0;
        int tempSecond = 0;

        #endregion fields & pins

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            timecodeList = FTCString[0].Split(':');

            if(FPlay.IsChanged) counter = Convert.ToInt32(timecodeList[3]);

            mytimecode.Hours = Convert.ToInt32(timecodeList[0]);
            mytimecode.Minutes = Convert.ToInt32(timecodeList[1]);
            mytimecode.Seconds = Convert.ToInt32(timecodeList[2]);
            if(FPlay[0]) mytimecode.Frames = counter;
            else mytimecode.Frames = Convert.ToInt32(timecodeList[3]);

            if (tempSecond != mytimecode.Seconds || counter >= 49) counter = 0;


            counter += 1;

            FTimeCode[0] = mytimecode;


            tempSecond = mytimecode.Seconds;

            //FLogger.Log(LogType.Debug, "hi tty!");
        }
    }
}
