using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DevActivity> DevActivities => Set<DevActivity>();
}
