using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jar
{
	public class CommandDataDisplay : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public enum Command
		{
			Idle, Finish, TargetTemperature, Keep, SetKp, SetTi, SetTd, SetPhaseDelay, SetPower
		}

		private Command type = Command.Idle;
		public Command Type { get => type;
			set
			{
				SetProperty("Type", ref type, value);
				OnPropertyChanged("VisibleTemperature");
				OnPropertyChanged("VisibleMinute");
				OnPropertyChanged("VisibleKp");
				OnPropertyChanged("VisibleTi");
				OnPropertyChanged("VisiblePhaseDelay");
				OnPropertyChanged("VisiblePower");
			}
		}

		private int index;
		public int Index { get => index; set => SetProperty("Index", ref index, value); }

		private int temperature = 75;
		public int Temperature { get => temperature; set => SetProperty("Temperature", ref temperature, value); }
		public bool VisibleTemperature { get => type == Command.TargetTemperature; }

		private int minute = 60;
		public int Minute { get => minute; set => SetProperty("Minute", ref minute, value); }
		public bool VisibleMinute { get => type == Command.Keep; }

		private float kp = 1.0f;
		public float Kp { get => kp; set => SetProperty("Kp", ref kp, value); }
		public bool VisibleKp { get => type == Command.SetKp; }

		private float ti = 0.1f;
		public float Ti { get => ti; set => SetProperty("Ti", ref ti, value); }
		public bool VisibleTi { get => type == Command.SetTi; }

		private int phaseDelay = 1200;
		public int PhaseDelay { get => phaseDelay; set => SetProperty("PhaseDelay", ref phaseDelay, value); }
		public bool VisiblePhaseDelay { get => type == Command.SetPhaseDelay; }
		
		private int power = 0;
		public int Power { get => power; set => SetProperty("Power", ref power, value); }
		public bool VisiblePower { get => type == Command.SetPower; }

		protected void OnPropertyChanged(string name)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}

		private void SetProperty<T>(string name, ref T var, T value)
		{
			if (object.Equals(var, value))
				return;
			var = value;
			OnPropertyChanged(name);
		}

		static public Dictionary<Command, string> CommandNameDictionary { get; private set; }
		static CommandDataDisplay()
		{
			CommandNameDictionary = new Dictionary<Command, string>();
			var enumvalues = Enum.GetValues(typeof(Command)) as Command[];
			foreach (var i in enumvalues)
				CommandNameDictionary.Add(i, i.ToString());
		}
	}
}
