using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace CustomIndicators.Scripts
{
    public class FractalN : Aindicator
    {
        private IndicatorParameterInt lengthFractal;
        private IndicatorDataSeries seriesUpFractal;
        private IndicatorDataSeries seriesDownFractal;
        private int halfLengthFractal;


        public override void OnStateChange(IndicatorState state)
        {
            if(state == IndicatorState.Configure)
            {
                lengthFractal = CreateParameterInt("Length Fractal", 5);
                seriesUpFractal = CreateSeries("UpFractal", Color.Aqua, IndicatorChartPaintType.Point, true);
                seriesDownFractal = CreateSeries("DownFractal", Color.Aqua, IndicatorChartPaintType.Point, true);
                if(lengthFractal.ValueInt % 2 == 0)
                {
                    lengthFractal.ValueInt++;
                }
                halfLengthFractal = lengthFractal.ValueInt / 2;
            }
            //else if(state == IndicatorState.Dispose)
            //{

            //}
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            // количество свечей должно быть не меньше длины фрактала
            if (index < lengthFractal.ValueInt)
            {
                return;
            }

            // получаем значения для центра возможного верхнего и нижнего фрактала  
            seriesUpFractal.Values[index - halfLengthFractal] = GetValueUpFractal(candles, index);
            seriesDownFractal.Values[index - halfLengthFractal] = GetValueDownFractal(candles, index);

            // если есть верхний фрактал, то удаляем предыдущие верхние фракталы в пределах половины длины фрактала
            if (seriesUpFractal.Values[index - halfLengthFractal] != 0)
            {
                for (int i = halfLengthFractal + 1; i < lengthFractal.ValueInt; i++)
                {
                    seriesUpFractal.Values[index - i] = 0;
                }
            }

            // если есть нижний фрактал, то удаляем предыдущие нижние фракталы в пределах половины длины фрактала
            if (seriesDownFractal.Values[index - halfLengthFractal] != 0)
            {
                for (int i = halfLengthFractal + 1; i < lengthFractal.ValueInt; i++)
                {
                    seriesDownFractal.Values[index - i] = 0;
                }
            }
        }

        private decimal GetValueUpFractal(List<Candle> candles, int index)
        {
            // фрактал считается сформированным только после того, как прошли количество свечей - halfLengthFractal
            // смотрим на свечу (index - halfLengthFractal)
            // при lenghtFractal = 5, halfLengthFractal = 2, первый возможный фрактал будет на третьей свече с индексом = 2

            // количество свечей должно быть не менее длины фрактала
            if (index < lengthFractal.ValueInt)
            {
                return 0;
            }

            // свеча, которая находится в середине возможного фрактала
            Candle candleCenterFractal = candles[index - halfLengthFractal];

            // у центральной свечи High должен быть больше, чем у соседних свечей в пределах длины фрактала от последней свечи
            for (int i = 0; i < lengthFractal.ValueInt; i++)
            {
                if(i == halfLengthFractal)
                {
                    continue;
                }
                if (candleCenterFractal.High < candles[index - i].High)
                {
                    return 0;
                }
            }

            return candleCenterFractal.High;
        }

        private decimal GetValueDownFractal(List<Candle> candles, int index)
        {
            // фрактал считается сформированным только после того, как прошли количество свечей - halfLengthFractal
            // смотрим на свечу (index - halfLengthFractal)
            // при lenghtFractal = 5, halfLengthFractal = 2, первый возможный фрактал будет на третьей свече с индексом = 2

            // количество свечей должно быть не менее длины фрактала
            if (index < lengthFractal.ValueInt)
            {
                return 0;
            }

            // свеча, которая находится в середине возможного фрактала
            Candle candleCenterFractal = candles[index - halfLengthFractal];

            // у центральной свечи Low должен быть меньше, чем у соседних свечей в пределах длины фрактала от последней свечи
            for (int i = 0; i < lengthFractal.ValueInt; i++)
            {
                if (i == halfLengthFractal)
                {
                    continue;
                }

                if (candleCenterFractal.Low > candles[index - i].Low)
                {
                    return 0;
                }
            }

            return candleCenterFractal.Low;
        }
    }
}
