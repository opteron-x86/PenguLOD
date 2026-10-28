// PenguLOD optional headless Vulkan compute backend. GPL-3.0-only.
#include "bc7_spv.h"
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <vulkan/vulkan_core.h>

typedef struct {
  VkInstance instance;
  VkPhysicalDevice physical;
  VkDevice device;
  VkQueue queue;
  VkDescriptorSetLayout descriptor_layout;
  VkPipelineLayout layout;
  VkPipeline pipeline;
  VkCommandPool command_pool;
  VkDescriptorPool descriptor_pool;
  VkPhysicalDeviceMemoryProperties memory;
  VkPhysicalDeviceProperties properties;
} Context;
typedef struct {
  VkBuffer buffer;
  VkDeviceMemory memory;
  void *mapped;
} Buffer;

void pgl_destroy(Context *c) {
  if (!c)
    return;
  if (c->device) {
    vkDeviceWaitIdle(c->device);
    if (c->pipeline)
      vkDestroyPipeline(c->device, c->pipeline, NULL);
    if (c->layout)
      vkDestroyPipelineLayout(c->device, c->layout, NULL);
    if (c->descriptor_layout)
      vkDestroyDescriptorSetLayout(c->device, c->descriptor_layout, NULL);
    if (c->command_pool)
      vkDestroyCommandPool(c->device, c->command_pool, NULL);
    if (c->descriptor_pool)
      vkDestroyDescriptorPool(c->device, c->descriptor_pool, NULL);
    vkDestroyDevice(c->device, NULL);
  }
  if (c->instance)
    vkDestroyInstance(c->instance, NULL);
  free(c);
}

#define CHECK(call)                                                            \
  do {                                                                         \
    VkResult rc = (call);                                                      \
    if (rc != VK_SUCCESS) {                                                    \
      snprintf(message, capacity, "%s failed (%d)", #call, rc);                \
      goto failed;                                                             \
    }                                                                          \
  } while (0)
Context *pgl_create(char *message, int capacity, int *software) {
  Context *c = calloc(1, sizeof(*c));
  if (!c)
    return NULL;
  VkShaderModule module = VK_NULL_HANDLE;
  VkApplicationInfo app = {.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
                           .pApplicationName = "PenguLOD",
                           .apiVersion = VK_API_VERSION_1_0};
  VkInstanceCreateInfo info = {.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
                               .pApplicationInfo = &app};
  CHECK(vkCreateInstance(&info, NULL, &c->instance));
  uint32_t count = 0;
  CHECK(vkEnumeratePhysicalDevices(c->instance, &count, NULL));
  if (count == 0 || count > 64) {
    snprintf(message, capacity, "No usable Vulkan device.");
    goto failed;
  }
  VkPhysicalDevice devices[64];
  CHECK(vkEnumeratePhysicalDevices(c->instance, &count, devices));
  uint32_t family = 0;
  int best = -1;
  for (uint32_t i = 0; i < count; i++) {
    VkPhysicalDeviceProperties p;
    vkGetPhysicalDeviceProperties(devices[i], &p);
    uint32_t families = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(devices[i], &families, NULL);
    if (families > 128)
      continue;
    VkQueueFamilyProperties queues[128];
    vkGetPhysicalDeviceQueueFamilyProperties(devices[i], &families, queues);
    int rank = p.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU     ? 4
               : p.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU ? 3
               : p.deviceType == VK_PHYSICAL_DEVICE_TYPE_CPU            ? 0
                                                                        : 1;
    for (uint32_t j = 0; j < families; j++)
      if ((queues[j].queueFlags & VK_QUEUE_COMPUTE_BIT) &&
          queues[j].queueCount && rank > best) {
        c->physical = devices[i];
        c->properties = p;
        family = j;
        best = rank;
        break;
      }
  }
  if (best < 0) {
    snprintf(message, capacity, "No compute queue.");
    goto failed;
  }
  *software = c->properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_CPU;
  vkGetPhysicalDeviceMemoryProperties(c->physical, &c->memory);
  float priority = 1;
  VkDeviceQueueCreateInfo queue = {
      .sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
      .queueFamilyIndex = family,
      .queueCount = 1,
      .pQueuePriorities = &priority};
  VkDeviceCreateInfo device = {.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
                               .queueCreateInfoCount = 1,
                               .pQueueCreateInfos = &queue};
  CHECK(vkCreateDevice(c->physical, &device, NULL, &c->device));
  vkGetDeviceQueue(c->device, family, 0, &c->queue);
  VkDescriptorSetLayoutBinding bindings[2] = {
      {.binding = 0,
       .descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
       .descriptorCount = 1,
       .stageFlags = VK_SHADER_STAGE_COMPUTE_BIT},
      {.binding = 1,
       .descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
       .descriptorCount = 1,
       .stageFlags = VK_SHADER_STAGE_COMPUTE_BIT}};
  VkDescriptorSetLayoutCreateInfo descriptors = {
      .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,
      .bindingCount = 2,
      .pBindings = bindings};
  CHECK(vkCreateDescriptorSetLayout(c->device, &descriptors, NULL,
                                    &c->descriptor_layout));
  VkPushConstantRange push = {.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT,
                              .size = 8};
  VkPipelineLayoutCreateInfo layout = {
      .sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
      .setLayoutCount = 1,
      .pSetLayouts = &c->descriptor_layout,
      .pushConstantRangeCount = 1,
      .pPushConstantRanges = &push};
  CHECK(vkCreatePipelineLayout(c->device, &layout, NULL, &c->layout));
  VkShaderModuleCreateInfo shader = {
      .sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,
      .codeSize = sizeof(bc7_spv),
      .pCode = bc7_spv};
  CHECK(vkCreateShaderModule(c->device, &shader, NULL, &module));
  VkComputePipelineCreateInfo pipeline = {
      .sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO,
      .layout = c->layout,
      .stage = {.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                .stage = VK_SHADER_STAGE_COMPUTE_BIT,
                .module = module,
                .pName = "main"}};
  CHECK(vkCreateComputePipelines(c->device, VK_NULL_HANDLE, 1, &pipeline, NULL,
                                 &c->pipeline));
  vkDestroyShaderModule(c->device, module, NULL);
  module = VK_NULL_HANDLE;
  VkCommandPoolCreateInfo pool = {
      .sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
      .queueFamilyIndex = family};
  CHECK(vkCreateCommandPool(c->device, &pool, NULL, &c->command_pool));
  VkDescriptorPoolSize size = {.type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
                               .descriptorCount = 2};
  VkDescriptorPoolCreateInfo descriptor_pool = {
      .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
      .maxSets = 1,
      .poolSizeCount = 1,
      .pPoolSizes = &size};
  CHECK(vkCreateDescriptorPool(c->device, &descriptor_pool, NULL,
                               &c->descriptor_pool));
  snprintf(message, capacity, "%s", c->properties.deviceName);
  return c;
failed:
  if (module)
    vkDestroyShaderModule(c->device, module, NULL);
  pgl_destroy(c);
  return NULL;
}

static void free_buffer(Context *c, Buffer *b) {
  if (b->mapped)
    vkUnmapMemory(c->device, b->memory);
  if (b->buffer)
    vkDestroyBuffer(c->device, b->buffer, NULL);
  if (b->memory)
    vkFreeMemory(c->device, b->memory, NULL);
}
static VkResult buffer(Context *c, VkDeviceSize size, Buffer *b) {
  VkBufferCreateInfo info = {.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                             .size = size,
                             .usage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT};
  VkResult result = vkCreateBuffer(c->device, &info, NULL, &b->buffer);
  if (result)
    return result;
  VkMemoryRequirements requirements;
  vkGetBufferMemoryRequirements(c->device, b->buffer, &requirements);
  uint32_t type = UINT32_MAX;
  for (uint32_t i = 0; i < c->memory.memoryTypeCount; i++)
    if ((requirements.memoryTypeBits & (1u << i)) &&
        (c->memory.memoryTypes[i].propertyFlags &
         (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
          VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) ==
            (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
             VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) {
      type = i;
      break;
    }
  if (type == UINT32_MAX)
    return VK_ERROR_FEATURE_NOT_PRESENT;
  VkMemoryAllocateInfo allocation = {.sType =
                                         VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                                     .allocationSize = requirements.size,
                                     .memoryTypeIndex = type};
  result = vkAllocateMemory(c->device, &allocation, NULL, &b->memory);
  if (result)
    return result;
  result = vkBindBufferMemory(c->device, b->buffer, b->memory, 0);
  if (result)
    return result;
  return vkMapMemory(c->device, b->memory, 0, size, 0, &b->mapped);
}

// Calls on a context are serialized by the managed wrapper. Only opaque input
// is accepted there.
int pgl_encode(Context *c, const uint8_t *rgba, uint32_t width, uint32_t height,
               uint8_t *output, char *message, int capacity) {
  Buffer input = {0}, encoded = {0};
  VkFence fence = VK_NULL_HANDLE;
  VkCommandBuffer command = VK_NULL_HANDLE;
  int success = 0, submitted = 0;
  if (!c || !rgba || !output || width == 0 || height == 0 || width > 4096 ||
      height > 4096) {
    snprintf(message, capacity, "Invalid image dimensions.");
    return 0;
  }
  uint64_t source_bytes = (uint64_t)width * height * 4,
           target_bytes = (uint64_t)((width + 3) / 4) * ((height + 3) / 4) * 16;
  if (source_bytes > c->properties.limits.maxStorageBufferRange ||
      target_bytes > c->properties.limits.maxStorageBufferRange) {
    snprintf(message, capacity, "Image exceeds storage buffer limit.");
    return 0;
  }
  CHECK(buffer(c, source_bytes, &input));
  CHECK(buffer(c, target_bytes, &encoded));
  memcpy(input.mapped, rgba, source_bytes);
  CHECK(vkResetCommandPool(c->device, c->command_pool, 0));
  CHECK(vkResetDescriptorPool(c->device, c->descriptor_pool, 0));
  VkDescriptorSetAllocateInfo allocation = {
      .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
      .descriptorPool = c->descriptor_pool,
      .descriptorSetCount = 1,
      .pSetLayouts = &c->descriptor_layout};
  VkDescriptorSet set;
  CHECK(vkAllocateDescriptorSets(c->device, &allocation, &set));
  VkDescriptorBufferInfo buffers[2] = {{input.buffer, 0, source_bytes},
                                       {encoded.buffer, 0, target_bytes}};
  VkWriteDescriptorSet writes[2] = {0};
  for (uint32_t i = 0; i < 2; i++)
    writes[i] = (VkWriteDescriptorSet){
        .sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
        .dstSet = set,
        .dstBinding = i,
        .descriptorCount = 1,
        .descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,
        .pBufferInfo = &buffers[i]};
  vkUpdateDescriptorSets(c->device, 2, writes, 0, NULL);
  VkCommandBufferAllocateInfo command_allocation = {
      .sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
      .commandPool = c->command_pool,
      .level = VK_COMMAND_BUFFER_LEVEL_PRIMARY,
      .commandBufferCount = 1};
  CHECK(vkAllocateCommandBuffers(c->device, &command_allocation, &command));
  VkCommandBufferBeginInfo begin = {
      .sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
      .flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT};
  CHECK(vkBeginCommandBuffer(command, &begin));
  VkMemoryBarrier barrier = {.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER,
                             .srcAccessMask = VK_ACCESS_HOST_WRITE_BIT,
                             .dstAccessMask = VK_ACCESS_SHADER_READ_BIT};
  vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_HOST_BIT,
                       VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &barrier, 0,
                       NULL, 0, NULL);
  vkCmdBindPipeline(command, VK_PIPELINE_BIND_POINT_COMPUTE, c->pipeline);
  vkCmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_COMPUTE, c->layout, 0,
                          1, &set, 0, NULL);
  uint32_t dims[2] = {width, height};
  vkCmdPushConstants(command, c->layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, 8,
                     dims);
  vkCmdDispatch(command, (uint32_t)((target_bytes / 16 + 63) / 64), 1, 1);
  barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
  barrier.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
  vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                       VK_PIPELINE_STAGE_HOST_BIT, 0, 1, &barrier, 0, NULL, 0,
                       NULL);
  CHECK(vkEndCommandBuffer(command));
  VkFenceCreateInfo fence_info = {.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
  CHECK(vkCreateFence(c->device, &fence_info, NULL, &fence));
  VkSubmitInfo submit = {.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO,
                         .commandBufferCount = 1,
                         .pCommandBuffers = &command};
  CHECK(vkQueueSubmit(c->queue, 1, &submit, fence));
  submitted = 1;
  CHECK(vkWaitForFences(c->device, 1, &fence, VK_TRUE, 30000000000ull));
  memcpy(output, encoded.mapped, target_bytes);
  success = 1;
failed:
  if (submitted && !success)
    vkDeviceWaitIdle(c->device);
  if (fence)
    vkDestroyFence(c->device, fence, NULL);
  if (command)
    vkFreeCommandBuffers(c->device, c->command_pool, 1, &command);
  free_buffer(c, &input);
  free_buffer(c, &encoded);
  return success;
}
