using System;
using Unity.VisualScripting;
using UnityEngine;

public class TimerEvent
{
    public TimerEvent()
    {
        // TODO later
    }
}

public class RoslikeTimer
{
    public RoslikeTimer(Action<TimerEvent> callback, bool discreteStepMode, float tickDelta)
    {
        this.callback = callback;
        if(discreteStepMode)
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
                callback(new TimerEvent());
            }
        }
        else
        {
            secondsPassed += elapsedSeconds;
            while (secondsPassed >= secondsPerTick)
            {
                secondsPassed -= secondsPerTick;
                callback(new TimerEvent());
            }
        }
    }

    public Action<TimerEvent> callback;
}
