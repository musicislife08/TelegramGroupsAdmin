# Docker Storage Driver Migration: ZFS → Overlay2 on ZFS

## Executive Summary

This document outlines a plan to migrate Docker from the native ZFS storage driver to overlay2 driver running on top of ZFS filesystem. This migration addresses the severe performance degradation caused by ZFS creating a snapshot for each Docker layer during image builds.

**Key Benefits:**
- **Dramatically faster builds**: Docker builds go from minutes to seconds
- **Reduced snapshot overhead**: Eliminates per-layer ZFS snapshots
- **Industry standard**: Overlay2 is Docker's default and most stable driver
- **Maintains ZFS benefits**: Data integrity, compression, snapshots at dataset level

**Key Risks:**
- Requires complete Docker rebuild (containers, images)
- Brief service downtime during migration
- Storage format incompatibility (no in-place upgrade)

---

## Current State Analysis

### Problem: ZFS Storage Driver Performance

When using Docker's native ZFS storage driver:
- Each Docker image layer creates a ZFS dataset clone
- Each layer operation triggers a ZFS snapshot
- Build operations are 10-100x slower than ext4/overlay2
- Example: Image builds taking 5+ minutes vs 5-10 seconds on overlay2

### Root Cause

ZFS storage driver creates deep nested dataset hierarchies:
```
tank/docker
  ├── abc123... (base layer snapshot)
  │   └── def456... (layer 2 snapshot)
  │       └── ghi789... (layer 3 snapshot)
  ...
```

Each layer operation waits for ZFS COW (copy-on-write) and snapshot creation, which is extremely slow on spinning disks and even noticeable on SSDs.

---

## Why Migrate to Overlay2 on ZFS

### Performance Benefits

**Modern Kernel Support (6.1+):**
- OpenZFS 2.2+ includes whiteout support for overlay2
- Docker overlay2 driver works seamlessly on ZFS backing filesystem
- No special kernel patches or configuration required

**Build Speed Improvements:**
- 10-100x faster image builds (minutes → seconds)
- Eliminates per-layer snapshot overhead
- File-level operations instead of block-level clones

**Write Performance:**
- Better performance for container writable layers
- More efficient memory usage
- Reduced disk I/O for temporary files

### Retained ZFS Benefits

**Filesystem Features:**
- Compression (lz4, zstd) at dataset level
- Snapshots for entire Docker root directory
- Data integrity checksums
- ARC caching for read performance
- Dataset quotas and reservations

---

## Technical Approach

### Architecture

**Before (ZFS Driver):**
```
Docker Engine
    ↓
ZFS Storage Driver
    ↓
ZFS Datasets (per-layer)
    ↓
ZFS Pool
```

**After (Overlay2 on ZFS):**
```
Docker Engine
    ↓
Overlay2 Storage Driver
    ↓
Regular filesystem (ext4-like semantics)
    ↓
ZFS Dataset (single)
    ↓
ZFS Pool
```

### Storage Layout

**Recommended ZFS Dataset Structure:**
```
tank/docker
├── overlay2/          # Main Docker storage
├── volumes/           # Docker volumes (separate dataset)
└── buildkit/          # BuildKit cache (optional)
```

---

## Prerequisites

### System Requirements

- **Kernel**: Linux 5.11+ (6.1+ recommended for best ZFS overlay2 support)
- **OpenZFS**: 2.2.0+ (for whiteout support)
- **Docker**: 20.10+ (24.0+ recommended)
- **Memory**: 4GB+ recommended (ZFS ARC needs memory)

### Before Migration Checklist

- [ ] Verify kernel version: `uname -r`
- [ ] Verify OpenZFS version: `zfs --version`
- [ ] Check current storage driver: `docker info | grep "Storage Driver"`
- [ ] Document current ZFS pool: `zpool status`
- [ ] List all running containers: `docker ps -a`
- [ ] Identify critical images: `docker images`
- [ ] Check available disk space (need 2x current Docker size)
- [ ] Schedule maintenance window
- [ ] Notify stakeholders

---

## ZFS Dataset Configuration

### Create Optimized ZFS Datasets

```bash
# Main Docker storage dataset
zfs create -o mountpoint=/var/lib/docker-new \
           -o recordsize=16K \
           -o compression=lz4 \
           -o atime=off \
           -o relatime=on \
           -o xattr=sa \
           -o dnodesize=auto \
           tank/docker

# Docker volumes (write-heavy workloads)
zfs create -o recordsize=16K \
           -o logbias=latency \
           -o sync=standard \
           tank/docker/volumes

# BuildKit cache (optional)
zfs create -o recordsize=128K \
           -o primarycache=metadata \
           tank/docker/buildkit
```

### ZFS Tuning Parameters Explained

| Parameter | Value | Reasoning |
|-----------|-------|-----------|
| `recordsize` | 16K | Matches Docker's small file workload, reduces write amplification |
| `compression` | lz4 | Fast compression, good for text/config files |
| `atime` | off | Reduces write overhead for read operations |
| `relatime` | on | Updates access time occasionally (still useful) |
| `xattr` | sa | Faster extended attribute access |
| `dnodesize` | auto | Optimal inode sizing for mixed workloads |
| `logbias` | latency | Prioritizes low latency over throughput (for volumes) |
| `sync` | standard | Balance between safety and performance |

**Note on recordsize:** 16K is optimal for Docker's small-file workload. Default 128K causes read/write amplification for container layers.

---

## Step-by-Step Migration Plan

### Phase 1: Backup Current State (30-60 minutes)

**1.1 Export All Images**
```bash
# Create backup directory
mkdir -p /backup/docker-images

# List all images
docker images --format "{{.Repository}}:{{.Tag}}" > /backup/image-list.txt

# Export each image
while read image; do
    filename=$(echo "$image" | tr '/:' '_')
    echo "Saving $image..."
    docker save "$image" | gzip > "/backup/docker-images/${filename}.tar.gz"
done < /backup/image-list.txt
```

**1.2 Document Container State**
```bash
# Export container configurations
docker inspect $(docker ps -aq) > /backup/containers-config.json

# Export volumes
docker volume ls -q > /backup/volumes-list.txt

# Backup Docker compose files
find /path/to/compose -name "docker-compose.yml" -exec cp {} /backup/compose/ \;
```

**1.3 Stop All Containers**
```bash
# Stop all running containers
docker stop $(docker ps -q)

# Verify nothing running
docker ps
```

### Phase 2: Create New ZFS Dataset (5 minutes)

**2.1 Create Dataset**
```bash
# Create optimized ZFS dataset
zfs create -o mountpoint=/var/lib/docker-new \
           -o recordsize=16K \
           -o compression=lz4 \
           -o atime=off \
           -o relatime=on \
           -o xattr=sa \
           -o dnodesize=auto \
           tank/docker-overlay2

# Verify creation
zfs list tank/docker-overlay2
df -h /var/lib/docker-new
```

### Phase 3: Reconfigure Docker (10 minutes)

**3.1 Stop Docker**
```bash
# Stop Docker daemon
systemctl stop docker.socket
systemctl stop docker.service

# Verify stopped
systemctl status docker
```

**3.2 Backup Current Docker Directory**
```bash
# Rename old Docker directory (keep as fallback)
mv /var/lib/docker /var/lib/docker-zfs-backup
```

**3.3 Configure Overlay2 Driver**
```bash
# Create daemon configuration
cat > /etc/docker/daemon.json <<'EOF'
{
  "storage-driver": "overlay2",
  "storage-opts": [
    "overlay2.override_kernel_check=true"
  ],
  "data-root": "/var/lib/docker-new",
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
EOF

# Verify configuration
cat /etc/docker/daemon.json
```

**3.4 Create Symlink (Optional)**
```bash
# Link new location to standard path
ln -s /var/lib/docker-new /var/lib/docker
```

### Phase 4: Start Docker with New Driver (5 minutes)

**4.1 Start Docker**
```bash
# Start Docker daemon
systemctl start docker.service

# Check status
systemctl status docker

# Verify overlay2 driver
docker info | grep -A 10 "Storage Driver"
```

**Expected output:**
```
Storage Driver: overlay2
  Backing Filesystem: zfs
  Supports d_type: true
  Using metacopy: false
  Native Overlay Diff: true
```

### Phase 5: Restore Images (30-120 minutes, depending on image count)

**5.1 Load Saved Images**
```bash
# Navigate to backup directory
cd /backup/docker-images

# Load each image
for tarfile in *.tar.gz; do
    echo "Loading $tarfile..."
    gunzip -c "$tarfile" | docker load
done

# Verify images restored
docker images
diff <(sort /backup/image-list.txt) <(docker images --format "{{.Repository}}:{{.Tag}}" | sort)
```

### Phase 6: Rebuild Containers (30-60 minutes)

**6.1 Recreate Containers**
```bash
# Option A: Docker Compose (recommended)
cd /path/to/compose
docker compose up -d

# Option B: Manual recreation using backed-up configs
# Review /backup/containers-config.json
# Manually recreate each container with correct parameters
```

**6.2 Restore Volumes**
```bash
# If volumes were backed up separately
docker volume create <volume-name>
# Copy data from backup location
```

### Phase 7: Verification (15-30 minutes)

**7.1 Test Container Functionality**
```bash
# Check all containers running
docker ps

# Test application endpoints
curl http://localhost:<port>/health

# Check logs for errors
docker logs <container-name>
```

**7.2 Performance Validation**
```bash
# Test build speed (use a representative Dockerfile)
time docker build -t test-build .

# Compare with previous build times
# Should see 10-100x improvement
```

**7.3 ZFS Dataset Verification**
```bash
# Check space usage
zfs list -o name,used,avail,refer,mountpoint tank/docker-overlay2

# Verify compression working
zfs get compressratio tank/docker-overlay2

# Check for errors
zpool status
```

### Phase 8: Cleanup (Optional, after successful validation)

**8.1 Remove Old ZFS Datasets**
```bash
# ONLY after confirming new setup works for 1-2 weeks

# List old Docker ZFS datasets
zfs list -r tank/docker | grep -v overlay2

# Destroy old datasets (IRREVERSIBLE)
zfs destroy -r tank/docker-old

# Unmount and remove old Docker directory
rm -rf /var/lib/docker-zfs-backup

# Remove image backups (after re-pulling/rebuilding)
rm -rf /backup/docker-images
```

---

## Rollback Strategy

### If Migration Fails

**Quick Rollback (5 minutes):**
```bash
# Stop Docker
systemctl stop docker

# Restore old configuration
rm /etc/docker/daemon.json
mv /var/lib/docker-zfs-backup /var/lib/docker

# Start Docker with ZFS driver
systemctl start docker

# Verify
docker info | grep "Storage Driver"
# Should show: Storage Driver: zfs
```

**Data Recovery:**
- Image backups in `/backup/docker-images/`
- Container configs in `/backup/containers-config.json`
- Original ZFS datasets preserved in `tank/docker-old`

---

## Testing Plan

### Pre-Migration Tests

1. **Inventory Check**
   - Document all running containers
   - List all images and tags
   - Export critical container configurations
   - Verify backup integrity

2. **Build Baseline**
   - Time a representative Docker build
   - Record current image pull times
   - Document container startup times

### Post-Migration Tests

1. **Functionality Tests**
   - All containers start successfully
   - Application endpoints respond correctly
   - Inter-container networking works
   - Volume mounts accessible
   - Secrets/configs available

2. **Performance Tests**
   - Build same Dockerfile, compare times
   - Monitor container startup latency
   - Test image pull/push operations
   - Validate write-heavy workload performance

3. **Stability Tests**
   - Run for 24-48 hours
   - Monitor for memory leaks
   - Check ZFS ARC usage
   - Verify no Docker daemon crashes

### Success Criteria

- ✅ All containers running and healthy
- ✅ Build times improved by 10x+
- ✅ No data loss
- ✅ Application functionality unchanged
- ✅ Docker daemon stable for 48+ hours
- ✅ ZFS compression working
- ✅ Disk space usage reasonable

---

## Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Image corruption during save/load | HIGH | LOW | Verify each backup with `docker load`, checksum validation |
| Container config loss | MEDIUM | LOW | Export to JSON, store Docker Compose files in git |
| Service downtime exceeds window | MEDIUM | MEDIUM | Schedule extra time, prepare rollback plan |
| Overlay2 incompatibility | HIGH | VERY LOW | Test on dev system first, verify kernel 6.1+ |
| ZFS recordsize suboptimal | LOW | MEDIUM | Benchmark different values (16K, 32K, 64K) |
| Docker daemon fails to start | HIGH | LOW | Keep old config as backup, test daemon.json syntax |

### Mitigation Strategies

**Before Migration:**
- Test on non-production system first
- Verify kernel and OpenZFS versions
- Schedule during low-traffic period
- Ensure full backups exist

**During Migration:**
- Keep old Docker directory as backup
- Validate each step before proceeding
- Monitor disk space continuously
- Document any deviations from plan

**After Migration:**
- Monitor for 48 hours before cleanup
- Keep image backups for 2 weeks
- Retain old ZFS datasets for 1 week
- Document lessons learned

---

## Post-Migration Optimization

### Docker Configuration

**Enable BuildKit (faster builds):**
```bash
# Add to /etc/docker/daemon.json
{
  "features": {
    "buildkit": true
  }
}
```

**BuildKit Cache on Separate Dataset:**
```bash
# Create dedicated dataset
zfs create -o recordsize=128K tank/docker/buildkit

# Configure BuildKit
export DOCKER_BUILDKIT=1
export BUILDKIT_CACHE_MOUNT_NS=/var/lib/docker-new/buildkit
```

### ZFS Tuning

**Monitor Compression Ratio:**
```bash
# Check compression effectiveness
zfs get compressratio tank/docker-overlay2

# If low (<1.2x), consider different algorithm
zfs set compression=zstd tank/docker-overlay2  # Better ratio, slower
```

**ARC Tuning (if memory constrained):**
```bash
# Limit ARC to 2GB (adjust based on system memory)
echo "options zfs zfs_arc_max=2147483648" > /etc/modprobe.d/zfs.conf
update-initramfs -u
```

**Regular Snapshots:**
```bash
# Create snapshot script
cat > /usr/local/bin/docker-snapshot.sh <<'EOF'
#!/bin/bash
zfs snapshot tank/docker-overlay2@$(date +%Y%m%d_%H%M%S)
# Keep last 7 days
zfs list -t snapshot -o name -s creation | grep tank/docker-overlay2@ | head -n -7 | xargs -n1 zfs destroy
EOF

chmod +x /usr/local/bin/docker-snapshot.sh

# Add to cron (daily at 3 AM)
echo "0 3 * * * /usr/local/bin/docker-snapshot.sh" | crontab -
```

### Monitoring

**Key Metrics to Track:**
- Build times (should be 10-100x faster)
- Container startup latency
- ZFS ARC hit ratio
- Disk space usage
- Compression ratio
- Docker daemon memory usage

**Monitoring Commands:**
```bash
# ZFS stats
zpool iostat -v 5
zfs get all tank/docker-overlay2

# Docker stats
docker stats --no-stream
docker system df

# ARC stats
arc_summary.py  # or cat /proc/spl/kstat/zfs/arcstats
```

---

## Estimated Timeline

| Phase | Duration | Description |
|-------|----------|-------------|
| **Preparation** | 1-2 hours | Read documentation, verify prerequisites |
| **Phase 1: Backup** | 30-60 min | Export images and container configs |
| **Phase 2: Dataset** | 5 min | Create ZFS dataset with optimal settings |
| **Phase 3: Reconfigure** | 10 min | Stop Docker, update daemon.json |
| **Phase 4: Start Docker** | 5 min | Start with overlay2 driver |
| **Phase 5: Restore Images** | 30-120 min | Load saved images (depends on count) |
| **Phase 6: Rebuild** | 30-60 min | Recreate containers |
| **Phase 7: Verification** | 15-30 min | Test functionality and performance |
| **Phase 8: Cleanup** | 30 min | Remove old datasets (after 1-2 weeks) |
| **Total** | **2.5-5 hours** | (excluding cleanup delay) |

**Recommended Window:** 4-6 hours during low-traffic period

---

## References

### Official Documentation
- [Docker Storage Drivers](https://docs.docker.com/storage/storagedriver/select-storage-driver/)
- [Docker OverlayFS Driver](https://docs.docker.com/storage/storagedriver/overlayfs-driver/)
- [Docker ZFS Driver](https://docs.docker.com/storage/storagedriver/zfs-driver/)
- [OpenZFS Documentation](https://openzfs.github.io/openzfs-docs/)

### Technical Resources
- [OpenZFS 2.2 Whiteout Support](https://github.com/openzfs/zfs/issues/8648)
- [ZFS Recordsize Tuning](https://klarasystems.com/articles/tuning-recordsize-in-openzfs/)
- [Docker Image Save/Load](https://docs.docker.com/reference/cli/docker/image/save/)

### Community Discussions
- [ZFS + Docker Overlay2 on Proxmox](https://forum.proxmox.com/threads/lxc-zfs-docker-overlay2-driver.122621/)
- [Docker Build Performance on ZFS](https://github.com/openzfs/zfs/issues/8648)
- [Overlay2 Unmount Performance](https://github.com/openzfs/zfs/issues/15581)

---

## Appendix A: Quick Reference Commands

### Pre-Migration
```bash
# Check current state
docker info | grep "Storage Driver"
zfs list -r tank/docker
docker images --format "table {{.Repository}}:{{.Tag}}\t{{.Size}}"
docker ps -a --format "table {{.Names}}\t{{.Status}}"

# Estimate required space
du -sh /var/lib/docker
```

### Migration
```bash
# Backup images
docker images --format "{{.Repository}}:{{.Tag}}" | while read img; do
    docker save "$img" | gzip > "/backup/$(echo $img | tr '/:' '_').tar.gz"
done

# Stop Docker
systemctl stop docker.socket docker.service

# Configure overlay2
echo '{"storage-driver":"overlay2","data-root":"/var/lib/docker-new"}' > /etc/docker/daemon.json

# Start Docker
systemctl start docker.service

# Restore images
for f in /backup/*.tar.gz; do gunzip -c "$f" | docker load; done
```

### Post-Migration
```bash
# Verify
docker info | grep -A 10 "Storage Driver"
docker images
docker ps -a
zfs list tank/docker-overlay2

# Performance test
time docker build -t test .
```

---

## Appendix B: Troubleshooting

### Common Issues

**Issue: Docker fails to start with overlay2**
```bash
# Check logs
journalctl -u docker.service -n 50

# Common cause: syntax error in daemon.json
jq . /etc/docker/daemon.json  # Validate JSON

# Verify kernel support
grep overlay /proc/filesystems
```

**Issue: Images fail to load**
```bash
# Verify backup integrity
gunzip -t /backup/image.tar.gz

# Try loading manually with verbose output
docker load -i /backup/image.tar.gz --quiet=false
```

**Issue: Poor performance after migration**
```bash
# Check ZFS ARC hit ratio (should be >80%)
arcstat 1 10

# Verify overlay2 using correct backing filesystem
docker info | grep "Backing Filesystem"  # Should show: zfs

# Check recordsize
zfs get recordsize tank/docker-overlay2  # Should be 16K
```

**Issue: High memory usage**
```bash
# ZFS ARC consuming too much memory
# Set limit (example: 2GB)
echo "options zfs zfs_arc_max=2147483648" > /etc/modprobe.d/zfs.conf
reboot

# Verify limit
cat /sys/module/zfs/parameters/zfs_arc_max
```

---

## Appendix C: Alternative Approaches

### Option A: Fresh Install (Simplest)
If you have no critical images or can easily rebuild:
1. Stop and remove all containers: `docker rm -f $(docker ps -aq)`
2. Remove all images: `docker rmi -f $(docker images -q)`
3. Stop Docker, update daemon.json, start Docker
4. Pull/rebuild images fresh
5. Recreate containers

**Pros:** Cleanest approach, fastest
**Cons:** Loses local images, requires rebuild time

### Option B: Hybrid ZFS/Overlay2
Keep some workloads on ZFS driver:
- Use overlay2 for build-heavy workloads
- Keep ZFS driver for production containers (snapshots, compression)
- Requires running two Docker instances or custom configuration

**Pros:** Flexibility, gradual migration
**Cons:** Complex setup, management overhead

### Option C: Migrate to Different Filesystem
If ZFS issues persist:
- Consider ext4 or xfs for Docker root
- Keep ZFS for data volumes only
- Simpler but loses ZFS benefits for Docker

**Pros:** Maximum performance, simpler
**Cons:** Loses ZFS features (compression, snapshots, checksums)

---

**Document Version:** 1.0
**Last Updated:** 2025-10-31
**Author:** Claude Code
**Status:** Ready for Review
