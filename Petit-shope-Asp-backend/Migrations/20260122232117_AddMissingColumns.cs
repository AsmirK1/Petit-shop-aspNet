using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petit_shope_Asp_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add any missing columns that might not exist in older databases
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='Users' AND column_name='AccountStatus') THEN
                        ALTER TABLE ""Users"" ADD COLUMN ""AccountStatus"" text NOT NULL DEFAULT 'Pending';
                    END IF;
                END
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
