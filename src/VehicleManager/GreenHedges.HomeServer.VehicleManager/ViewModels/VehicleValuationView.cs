using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class VehicleValuationView
{
    public int Id { get; init; }
    public int VehicleId { get; init; }
    public DateOnly Date { get; init; }
    public int Mileage { get; init; }
    public decimal Value { get; init; }
    public ValuationType Type { get; init; }
    public ValuationGrade? Grade { get; init; }
    public ValuationSource Source { get; init; }

    /// <summary>E.g. "Trade (Average)", or "Private" without a grade.</summary>
    public string Label => Grade is { } grade ? $"{TypeNames(Type)} ({grade})" : TypeNames(Type);

    public string SourceName => SourceNames(Source);

    public static string TypeNames(ValuationType type) => type == ValuationType.PartExchange ? "Part exchange" : type.ToString();

    public static string SourceNames(ValuationSource source) => source switch
    {
        ValuationSource.AutoTrader => "Auto Trader",
        ValuationSource.WhatCar => "What Car?",
        _ => source.ToString()
    };

    public static readonly Expression<Func<VehicleValuation, VehicleValuationView>> Projection = v => new VehicleValuationView
    {
        Id = v.Id,
        VehicleId = v.VehicleId,
        Date = v.Date,
        Mileage = v.Mileage,
        Value = v.Value,
        Type = v.Type,
        Grade = v.Grade,
        Source = v.Source
    };
}
