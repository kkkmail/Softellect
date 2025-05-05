using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Softellect.Migrations.WorkerNodeService.Migrations;

namespace Softellect.Migrations.WorkerNodeService.Context;

public partial class MyDbContext : DbContext
{
    public MyDbContext()
    {
    }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<DeliveryType> DeliveryType { get; set; }

    public virtual DbSet<Message> Message { get; set; }

    public virtual DbSet<ModelData> ModelData { get; set; }

    public virtual DbSet<NotificationType> NotificationType { get; set; }

    public virtual DbSet<RunQueue> RunQueue { get; set; }

    public virtual DbSet<RunQueueStatus> RunQueueStatus { get; set; }

    public virtual DbSet<Setting> Setting { get; set; }

    public virtual DbSet<Solver> Solver { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Data Source=localhost;Initial Catalog=wns901_04;Integrated Security=SSPI;Max Pool Size=20000;Connection Timeout=360;TrustServerCertificate=yes;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryType>(entity =>
        {
            entity.Property(e => e.deliveryTypeId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.Property(e => e.messageId).ValueGeneratedNever();
            entity.Property(e => e.messageOrder).ValueGeneratedOnAdd();

            entity.HasOne(d => d.deliveryType).WithMany(p => p.Message)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Message_DeliveryType");
        });

        modelBuilder.Entity<ModelData>(entity =>
        {
            entity.Property(e => e.runQueueId).ValueGeneratedNever();

            entity.HasOne(d => d.runQueue).WithOne(p => p.ModelData)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ModelData_RunQueue");
        });

        modelBuilder.Entity<NotificationType>(entity =>
        {
            entity.Property(e => e.notificationTypeId).ValueGeneratedNever();
        });

        modelBuilder.Entity<RunQueue>(entity =>
        {
            entity.Property(e => e.runQueueId).ValueGeneratedNever();
            entity.Property(e => e.createdOn).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.modifiedOn).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.relativeInvariant).HasDefaultValue(1.0);
            entity.Property(e => e.runQueueOrder).ValueGeneratedOnAdd();

            entity.HasOne(d => d.notificationType).WithMany(p => p.RunQueue)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RunQueue_NotificationType");

            entity.HasOne(d => d.runQueueStatus).WithMany(p => p.RunQueue)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RunQueue_RunQueueStatus");

            entity.HasOne(d => d.solver).WithMany(p => p.RunQueue)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RunQueue_Solver");
        });

        modelBuilder.Entity<RunQueueStatus>(entity =>
        {
            entity.Property(e => e.runQueueStatusId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.Property(e => e.createdOn).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Solver>(entity =>
        {
            entity.Property(e => e.solverId).ValueGeneratedNever();
            entity.Property(e => e.createdOn).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.solverOrder).ValueGeneratedOnAdd();
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
