#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using System.Threading;
using System.Windows.Forms;
using threadedForm;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "ThreadedForm",
                Category = "Value",
                Help = "Basic template with one value in/out",
                Tags = "c#",
        AutoEvaluate = true)]
    #endregion PluginInfo
    public class Template : IPluginEvaluate, IDisposable
    {
        #region fields & pins
        [Input("Input")]
        public ISpread<int> FInput;

        [Output("Output")]
        public ISpread<double> FOutput;


        private Thread FThread;

        private bool isInit = false;

        

        [Import()]
        public ILogger FLogger;
        #endregion fields & pins

        //called when data for any output pin is requested

        Form1 MyForm;

        public void Evaluate(int SpreadMax)
        {
            FOutput.SliceCount = SpreadMax;


            if (!isInit)
            {
                MyForm = null;

                FThread = new Thread(new ThreadStart(ShowForm));
                FThread.Name = "UIThread";
                FThread.SetApartmentState(ApartmentState.STA);

                if (FThread != null && !FThread.IsAlive) FThread.Start();


                isInit = true;
            }

            if(MyForm != null)
            {
                MyForm.Invoke(new Action(() => MyForm.Hallo = FInput[0]));
                FLogger.Log(LogType.Debug, "doing stuff");
            }
               
            else FLogger.Log(LogType.Debug, "not there");




            //FLogger.Log(LogType.Debug, "doing stuff");

            for (int i = 0; i < SpreadMax; i++)
                FOutput[i] = FInput[i] * 2;

            
        }




        public void ShowForm()
        {

            MyForm = new Form1();
            

        }

        public void Dispose()
        {
            //FThread.Abort();

            if (MyForm != null) MyForm.Dispose();

            //throw new NotImplementedException();

        }
    }
}
