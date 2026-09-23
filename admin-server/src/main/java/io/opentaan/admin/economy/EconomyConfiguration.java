package io.opentaan.admin.economy;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Configuration;

@Configuration
@EnableConfigurationProperties(EconomyProperties.class)
class EconomyConfiguration {
}
