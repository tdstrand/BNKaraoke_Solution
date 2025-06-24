using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.Extensions.Configuration;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/settings")]
    [ApiController]
    public class SettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SettingsController> _logger;
        private readonly IConfiguration _configuration;

        public SettingsController(ApplicationDbContext context, ILogger<SettingsController> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _logger.LogInformation("SettingsController initialized with connection string: {ConnectionString}", _configuration.GetConnectionString("DefaultConnection"));
        }

        [HttpGet("karaoke-channels")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> GetKaraokeChannels()
        {
            _logger.LogInformation("GetKaraokeChannels: Fetching all active karaoke channels");

            try
            {
                var channels = await _context.KaraokeChannels
                    .Where(kc => kc.IsActive)
                    .OrderBy(kc => kc.SortOrder)
                    .ToListAsync();
                if (channels == null)
                {
                    _logger.LogWarning("GetKaraokeChannels: KaraokeChannels table is empty or inaccessible");
                    return Ok(new List<KaraokeChannel>());
                }

                _logger.LogInformation("GetKaraokeChannels: Successfully fetched {ChannelCount} active channels", channels.Count);
                return Ok(channels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetKaraokeChannels: Exception occurred while fetching channels: {Message}\nStackTrace: {StackTrace}", ex.Message, ex.StackTrace);
                return StatusCode(500, new { error = "Failed to retrieve karaoke channels", details = ex.Message });
            }
        }

        [HttpPost("karaoke-channels")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> CreateKaraokeChannel([FromBody] KaraokeChannel channel)
        {
            _logger.LogInformation("CreateKaraokeChannel: Creating new channel with name {ChannelName}", channel.ChannelName);

            try
            {
                if (string.IsNullOrEmpty(channel.ChannelName))
                {
                    _logger.LogWarning("CreateKaraokeChannel: ChannelName is required");
                    return BadRequest(new { error = "ChannelName is required" });
                }

                channel.IsActive = true;
                _context.KaraokeChannels.Add(channel);
                await _context.SaveChangesAsync();

                _logger.LogInformation("CreateKaraokeChannel: Successfully created channel with Id {ChannelId}", channel.Id);
                return CreatedAtAction(nameof(GetKaraokeChannels), new { id = channel.Id }, channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateKaraokeChannel: Exception occurred while creating channel");
                return StatusCode(500, new { error = "Failed to create karaoke channel" });
            }
        }

        [HttpPut("karaoke-channels/{id}")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> UpdateKaraokeChannel(int id, [FromBody] KaraokeChannel channel)
        {
            _logger.LogInformation("UpdateKaraokeChannel: Updating channel with Id {ChannelId}", id);

            try
            {
                if (id != channel.Id)
                {
                    _logger.LogWarning("UpdateKaraokeChannel: Id mismatch between route {RouteId} and body {BodyId}", id, channel.Id);
                    return BadRequest(new { error = "Id mismatch" });
                }

                var existingChannel = await _context.KaraokeChannels.FindAsync(id);
                if (existingChannel == null)
                {
                    _logger.LogWarning("UpdateKaraokeChannel: Channel not found with Id {ChannelId}", id);
                    return NotFound(new { error = "Channel not found" });
                }

                existingChannel.ChannelName = channel.ChannelName;
                existingChannel.ChannelId = channel.ChannelId;
                existingChannel.SortOrder = channel.SortOrder;
                existingChannel.IsActive = channel.IsActive;

                await _context.SaveChangesAsync();

                _logger.LogInformation("UpdateKaraokeChannel: Successfully updated channel with Id {ChannelId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateKaraokeChannel: Exception occurred while updating channel with Id {ChannelId}", id);
                return StatusCode(500, new { error = "Failed to update karaoke channel" });
            }
        }

        [HttpDelete("karaoke-channels/{id}")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> DeleteKaraokeChannel(int id)
        {
            _logger.LogInformation("DeleteKaraokeChannel: Deactivating channel with Id {ChannelId}", id);

            try
            {
                var channel = await _context.KaraokeChannels.FindAsync(id);
                if (channel == null)
                {
                    _logger.LogWarning("DeleteKaraokeChannel: Channel not found with Id {ChannelId}", id);
                    return NotFound(new { error = "Channel not found" });
                }

                channel.IsActive = false;
                await _context.SaveChangesAsync();

                _logger.LogInformation("DeleteKaraokeChannel: Successfully deactivated channel with Id {ChannelId}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteKaraokeChannel: Exception occurred while deactivating channel with Id {ChannelId}", id);
                return StatusCode(500, new { error = "Failed to deactivate karaoke channel" });
            }
        }

        [HttpPut("karaoke-channels/reorder")]
        [Authorize(Policy = "SongManager")]
        public async Task<IActionResult> ReorderKaraokeChannels([FromBody] List<ChannelOrder> orders)
        {
            _logger.LogInformation("ReorderKaraokeChannels: Reordering {OrderCount} channels", orders.Count);

            try
            {
                if (orders == null || !orders.Any())
                {
                    _logger.LogWarning("ReorderKaraokeChannels: No orders provided");
                    return BadRequest(new { error = "No orders provided" });
                }

                var channelIds = orders.Select(o => o.Id).ToList();
                var channels = await _context.KaraokeChannels
                    .Where(kc => channelIds.Contains(kc.Id))
                    .ToListAsync();

                if (channels.Count != orders.Count)
                {
                    _logger.LogWarning("ReorderKaraokeChannels: Some channel IDs not found");
                    return BadRequest(new { error = "Some channel IDs not found" });
                }

                foreach (var channel in channels)
                {
                    var order = orders.First(o => o.Id == channel.Id);
                    channel.SortOrder = order.SortOrder;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("ReorderKaraokeChannels: Successfully reordered {ChannelCount} channels", channels.Count);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReorderKaraokeChannels: Exception occurred while reordering channels");
                return StatusCode(500, new { error = "Failed to reorder karaoke channels" });
            }
        }
    }

    public class ChannelOrder
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
    }
}