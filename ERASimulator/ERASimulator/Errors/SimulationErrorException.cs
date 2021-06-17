using System;

namespace ERASimulator.Errors
{
    class SimulationErrorException : Exception
    {
        protected string message;

        public SimulationErrorException(string message)
        {
            this.message = message;
        }

        override public string ToString()
        {
            return message;
        }

    }
}
