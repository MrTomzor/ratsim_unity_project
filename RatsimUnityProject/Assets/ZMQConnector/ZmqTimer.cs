using System;

public class ZmqTimerEvent
{
    public ZmqTimerEvent()
    {
    }
}

public class ZmqTimer
{
    public ZmqTimer(Action<ZmqTimerEvent> callback, bool discreteStepMode, float tickDelta)
    {
        this.callback = callback;
        if (discreteStepMode)
        {
            discreteMode = true;
            stepsPerTick = (uint)tickDelta;
        }
        else
        {
            discreteMode = false;
            secondsPerTick = tickDelta;
        }
    }

    public bool discreteMode = true;
    public float secondsPerTick = 0.1f;
    public float secondsPassed = 0.0f;
    public uint stepsPerTick = 1;
    public uint stepsPassed = 0;

    public void HandleSteps(uint elapsedPhysicsSteps, float elapsedSeconds)
    {
        if (discreteMode)
        {
            stepsPassed += elapsedPhysicsSteps;
            while (stepsPassed >= stepsPerTick)
            {
                stepsPassed -= stepsPerTick;
                callback(new ZmqTimerEvent());
            }
        }
        else
        {
            secondsPassed += elapsedSeconds;
            while (secondsPassed >= secondsPerTick)
            {
                secondsPassed -= secondsPerTick;
                callback(new ZmqTimerEvent());
            }
        }
    }

    public Action<ZmqTimerEvent> callback;
}

