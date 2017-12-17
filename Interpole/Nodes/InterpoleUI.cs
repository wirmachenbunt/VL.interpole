using System;
using System.Threading;
using System.ComponentModel.Composition;
using System.Windows.Forms;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using Interpole;
using VVVV.Core.Logging;

namespace VVVV.Nodes
{
    [PluginInfo(Name = "InterpoleUI", Category = "Interpole", AutoEvaluate = true)]

    public class InterpoleUI : IPluginEvaluate, IDisposable
    {
        Thread FUIThread;
        InterpoleForm FMyForm;
        Label FMyLabel;

        [Input("Some Text")]
        ISpread<string> FSomeTextIn;

        [Output("Out")]
        ISpread<int> FOut;

        [Input("Value")]
        IDiffSpread<int> FValue;

        public InterpoleUI()
        {
            FUIThread = new Thread(InitializeForm);
            FUIThread.SetApartmentState(ApartmentState.STA);
            //FUIThread.IsBackground = true;
            FUIThread.Start();
        }

        public void Dispose()
        {
            if (FMyForm != null)
                FMyForm.Invoke((Action)CleanupForm);
        }

        void InitializeForm()
        {
            FMyForm = new InterpoleForm();
            FMyLabel = new Label() { Text = "Lala" };
            FMyForm.Controls.Add(FMyLabel);
            // Use ShowDialog as it starts its own main loop - using Show alone the window just disappears
            FMyForm.ShowDialog();
        }

        void CleanupForm()
        {
            FMyForm.Dispose();
        }


        void InvalidateForm()
        {
            FMyForm.Invalidate();
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            if (FMyForm == null)
                return; // Form not created yet

            //FMyForm.Invoke((Action)InvalidateForm);

            //FMyForm.Invalidate();
            foreach (var value in FSomeTextIn)
            {
                //FMyForm.Invoke((Action)(() => FMyLabel.Text = value));
            }
            
        }
    }
}