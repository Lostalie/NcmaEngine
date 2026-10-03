#pragma once

#include "../RenderAPI.h"
#include <vulkan/vulkan.h>
#include <GLFW/glfw3.h>

#include <vector>
#include <string>
#include <optional>

namespace NcmaEngine
{
	class VulkanRenderAPI : public RenderAPI
	{
	public:
		VulkanRenderAPI();
		virtual ~VulkanRenderAPI();

		virtual void Init() override;
		virtual void Shutdown() override;

		virtual void Clear() override;
		virtual void SetClearColor(float r, float g, float b, float a) override;

		virtual void SetViewport(uint32_t x, uint32_t y, uint32_t width, uint32_t height) override;

		virtual void EnableDepthTest(bool enable) override;
		virtual void EnableBlending(bool enable) override;

		// Vulkan specific
		VkInstance GetInstance() const { return m_Instance; }
		VkPhysicalDevice GetPhysicalDevice() const { return m_PhysicalDevice; }
		VkDevice GetDevice() const { return m_Device; }
		VkQueue GetGraphicsQueue() const { return m_GraphicsQueue; }
		VkQueue GetPresentQueue() const { return m_PresentQueue; }
		VkCommandPool GetCommandPool() const { return m_CommandPool; }
		VkCommandBuffer GetCommandBuffer() const { return m_CommandBuffer; }
		VkFramebuffer GetCurrentFramebuffer() const { return m_SwapChainFramebuffers[m_CurrentFrame]; }
		VkRenderPass GetRenderPass() const { return m_RenderPass; }
		uint32_t GetCurrentFrame() const { return m_CurrentFrame; }

		void WaitForGPU();
		void SubmitAndPresent();

	private:
		void CreateInstance();
		void SetupDebugMessenger();
		void CreateSurface();
		void PickPhysicalDevice();
		void CreateLogicalDevice();
		void CreateSwapChain();
		void CreateRenderPass();
		void CreateFramebuffers();
		void CreateCommandPool();
		void CreateCommandBuffer();

		bool CheckValidationLayerSupport();
		bool IsDeviceSuitable(VkPhysicalDevice device);
		bool CheckDeviceExtensionSupport(VkPhysicalDevice device);

		static VKAPI_ATTR VkBool32 VKAPI_CALL DebugCallback(
			VkDebugUtilsMessageSeverityFlagBitsEXT messageSeverity,
			VkDebugUtilsMessageTypeFlagsEXT messageType,
			const VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
			void* pUserData);

		struct QueueFamilyIndices
		{
			std::optional<uint32_t> GraphicsFamily;
			std::optional<uint32_t> PresentFamily;
			bool IsComplete() { return GraphicsFamily.has_value() && PresentFamily.has_value(); }
		};

		static const uint32_t s_MaxFramesInFlight = 2;

		// Vulkan objects
		VkInstance m_Instance = VK_NULL_HANDLE;
		VkPhysicalDevice m_PhysicalDevice = VK_NULL_HANDLE;
		VkDevice m_Device = VK_NULL_HANDLE;
		VkSurfaceKHR m_Surface = VK_NULL_HANDLE;
		VkQueue m_GraphicsQueue = VK_NULL_HANDLE;
		VkQueue m_PresentQueue = VK_NULL_HANDLE;
		VkCommandPool m_CommandPool = VK_NULL_HANDLE;
		VkCommandBuffer m_CommandBuffer = VK_NULL_HANDLE;

		// Swap chain
		VkSwapchainKHR m_SwapChain = VK_NULL_HANDLE;
		std::vector<VkImage> m_SwapChainImages;
		VkFormat m_SwapChainImageFormat;
		VkExtent2D m_SwapChainExtent;
		std::vector<VkImageView> m_SwapChainImageViews;
		std::vector<VkFramebuffer> m_SwapChainFramebuffers;

		// Render pass
		VkRenderPass m_RenderPass = VK_NULL_HANDLE;

		// Synchronization
		std::vector<VkSemaphore> m_ImageAvailableSemaphores;
		std::vector<VkSemaphore> m_RenderFinishedSemaphores;
		std::vector<VkFence> m_InFlightFences;

		// Debug
		VkDebugUtilsMessengerEXT m_DebugMessenger = VK_NULL_HANDLE;

		// State
		uint32_t m_CurrentFrame = 0;
		float m_ClearColor[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
		bool m_DepthTestEnabled = true;
		bool m_BlendingEnabled = false;

		const std::vector<const char*> m_ValidationLayers = { "VK_LAYER_KHRONOS_validation" };
		const std::vector<const char*> m_DeviceExtensions = { VK_KHR_SWAPCHAIN_EXTENSION_NAME };
	};
}
