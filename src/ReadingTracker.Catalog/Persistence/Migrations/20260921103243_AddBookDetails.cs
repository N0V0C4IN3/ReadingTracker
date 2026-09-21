using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadingTracker.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every Book already cached has no categories yet, which is an empty list, not
            // a null: without the default Postgres would refuse to add a NOT NULL column to a
            // table with rows in it.
            migrationBuilder.AddColumn<List<string>>(
                name: "Categories",
                schema: "catalog",
                table: "Books",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "catalog",
                table: "Books",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DetailsLookedAt",
                schema: "catalog",
                table: "Books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedDate",
                schema: "catalog",
                table: "Books",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                schema: "catalog",
                table: "Books",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Categories",
                schema: "catalog",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "catalog",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "DetailsLookedAt",
                schema: "catalog",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PublishedDate",
                schema: "catalog",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Publisher",
                schema: "catalog",
                table: "Books");
        }
    }
}
