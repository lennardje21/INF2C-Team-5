using Microsoft.AspNetCore.Mvc;
using CargoHub.Api.Models;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("api/v1/warehouses")]
public class WarehousesController : ControllerBase
{
    private static readonly List<WarehousesModel> Warehouses =
    [
        new WarehousesModel
        {
            Id = 1,
            Code = "VGH-AMB",
            Name = "Jumbo DC Veghel Ambient",
            Address = "De Amert 409",
            City = "Veghel",
            ZipCode = "5462 GH",
            Province = "Noord-Brabant",
            Country = "Netherlands",
            ContactName = "Ali Schellekens",
            ContactPhone = "(032) 1819600",
            ContactEmail = "vgh-amb@jumbo-logistiek.nl"
        }
    ];

    [HttpGet]
    public ActionResult<IEnumerable<WarehousesModel>> GetWarehouses()
    {
        return Ok(Warehouses);
    }

    [HttpGet("{id:int}")]
    public ActionResult<WarehousesModel> GetWarehouse(int id)
    {
        var warehouse = Warehouses.FirstOrDefault(x => x.Id == id);

        if (warehouse is null)
        {
            return NotFound();
        }

        return Ok(warehouse);
    }

    [HttpPost]
    public ActionResult<WarehousesModel> CreateWarehouse(
        WarehousesModel warehouse)
    {
        if (Warehouses.Any(x => x.Id == warehouse.Id))
        {
            return Conflict($"Warehouse with id {warehouse.Id} already exists.");
        }

        Warehouses.Add(warehouse);

        return CreatedAtAction(
            nameof(GetWarehouse),
            new { id = warehouse.Id },
            warehouse);
    }

    [HttpPut("{id:int}")]
    public IActionResult UpdateWarehouse(
        int id,
        WarehousesModel updatedWarehouse)
    {
        var warehouse = Warehouses.FirstOrDefault(x => x.Id == id);

        if (warehouse is null)
        {
            return NotFound();
        }

        updatedWarehouse.Id = id;

        var index = Warehouses.IndexOf(warehouse);
        Warehouses[index] = updatedWarehouse;

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult DeleteWarehouse(int id)
    {
        var warehouse = Warehouses.FirstOrDefault(x => x.Id == id);

        if (warehouse is null)
        {
            return NotFound();
        }

        Warehouses.Remove(warehouse);

        return NoContent();
    }
}